using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Reflection;

namespace ODCore.Serialization
{
    #region Define

    [System.Serializable]
    public struct ChunkedBytes
    {
        public const int ByteCount_ChunckCount = 2;
        public const int ByteCount_ChunckSize = 4;

        [System.Serializable]
        public struct Chunk
        {
            public int Size
            {
                get
                {
                    return Bytes == null ? 0 : Bytes.Length;
                }
            }

            public Chunk(byte[] bytes)
            {
                Bytes = bytes;
            }

            public byte[] Bytes;
        }

        public int ChunkCount
        {
            get
            {
                return Chunks == null ? 0 : Chunks.Count;
            }
        }

        public List<Chunk> Chunks;

        public byte[] ToBytes()
        {
            return ChunkedBytes.ToBytes(this);
        }

        public static byte[] ToBytes(ChunkedBytes chunkedBytes)
        {
            int chunckCount = chunkedBytes.ChunkCount;

            int totalSize = 0;
            totalSize += ByteCount_ChunckCount;
            totalSize += ByteCount_ChunckSize * chunckCount;

            for (int i = 0; i < chunckCount; i++)
            {
                totalSize += chunkedBytes.Chunks[i].Size;
            }

            byte[] result = new byte[totalSize];
            XStreams.BitStream stream = new XStreams.BitStream(result);
            stream.WriteInt(chunckCount, ByteCount_ChunckCount);
            for (int i = 0; i < chunckCount; i++)
            {
                Chunk chunk = chunkedBytes.Chunks[i];
                stream.WriteInt(chunk.Size, ByteCount_ChunckSize);
                stream.WriteBytes(chunk.Bytes);
            }

            return result;
        }

        public static ChunkedBytes FromBytes(byte[] bytes)
        {
            ChunkedBytes result = new ChunkedBytes()
            {
                Chunks = new List<Chunk>(),
            };

            XStreams.BitStream stream = new XStreams.BitStream(bytes);

            int chunckCount = stream.GetInt(ByteCount_ChunckCount);
            for(int i = 0; i < chunckCount; i++)
            {
                int chunckSize = stream.GetInt(ByteCount_ChunckSize);
                Chunk newChunk = new Chunk()
                {
                    Bytes = stream.GetBytes(chunckSize),
                };
                result.Chunks.Add(newChunk);
            }

            return result;
        }
    }

    [System.Serializable]
    public struct AssetPath
    {
        public string Path;

        public string AssetDir
        {
            get
            {
                int slashIndex = Path.LastIndexOf('/');
                if (slashIndex == -1)
                {
                    return null;
                }

                return Path.Substring(0, slashIndex);
            }
        }

        public string AssetName
        {
            get
            {
                int slashIndex = Path.LastIndexOf('/');
                if (slashIndex == -1)
                {
                    return Path;
                }

                return Path.Substring(slashIndex + 1);
            }
        }

        

        public static implicit operator AssetPath(string assetPath)
        {
            return new AssetPath()
            {
                Path = assetPath,
            };
        }
    }

    [System.Serializable]
    public struct ODTableName
    {
        public AssetPath Path;
    }

    [System.Serializable]
    public struct ODTableHeader
    {
        public string TypeFullName;
    }

    [System.Serializable]
    public struct ODTable<T>
        where T : struct
    {
        public ODTableHeader Header;
        public Dictionary<string, T> Items;
    }

    [System.Serializable]
    public struct ODTableItemIndex
    {
        public ODTableName TableName;
        public string ItemName;
    }

    [System.Serializable]
    public struct ODTableItemIndexT<T>
        where T : struct
    {
        public ODTableItemIndex TableIndex;

        public ODTableName TableName
        {
            get
            {
                return TableIndex.TableName;
            }

            set
            {
                TableIndex.TableName = value;
            }
        }

        public string ItemName
        {
            get
            {
                return TableIndex.ItemName;
            }

            set
            {
                TableIndex.ItemName = value;
            }
        }
    }

    #endregion

    public class ODDatabase
    {
        #region Define

        private struct DBTable
        {
            public Type ObjectType;
            // Always formatted as Dictionary<string, T>
            public object Data;
        }

        #endregion

        #region Helper

        private T DeserializeMetaData<T>(byte[] bytes)
        {
            string jsonStr = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(jsonStr);
        }

        private byte[] SerializeMetaData<T>(T data)
        {
            string jsonStr = JsonConvert.SerializeObject(data);
            return Encoding.UTF8.GetBytes(jsonStr);
        }

        #endregion

        public ODSerializer Serializer;
        public ODAssetLoader AssetLoader;

        private Dictionary<ODTableName, DBTable> m_caches = new Dictionary<ODTableName, DBTable>();

        public ODDatabase(ODSerializer serializer, ODAssetLoader assetLoader)
        {
            Serializer = serializer;
            AssetLoader = assetLoader;
        }

        private DBTable? EnsureLoad(ODTableName tableName)
        {
            DBTable outUnit;
            if (!m_caches.TryGetValue(tableName, out outUnit))
            {
                ForceLoad(tableName);
                outUnit = m_caches[tableName];
            }

            return outUnit;
        }

        public void ForceLoad(ODTableName tableName)
        {
            if (m_caches.ContainsKey(tableName))
            {
                m_caches.Remove(tableName);
            }

            byte[] data = AssetLoader.Load(tableName.Path);
            ChunkedBytes chunkedBytes = ChunkedBytes.FromBytes(data);

            ODTableHeader dataHeader = DeserializeMetaData<ODTableHeader>(chunkedBytes.Chunks[0].Bytes);
            Type dataType = Type.GetType(dataHeader.TypeFullName, true);

            object datas = Serializer.DeSerialize(dataType, chunkedBytes.Chunks[1].Bytes, 0, chunkedBytes.Chunks[1].Size);

            DBTable outUnit;
            outUnit.ObjectType = dataType;
            outUnit.Data = datas;
            m_caches.Add(tableName, outUnit);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="dataType"></param>
        /// <param name="dictObject">Must be of type Dictionary<string, T></param>
        public bool SaveDataTable(ODTableName tableName, object dictObject)
        {
            if (dictObject == null)
            {
                return false;
            }

            Type dictType = dictObject.GetType();
            if (dictType.GetGenericTypeDefinition() != typeof(Dictionary<,>) || dictType.GetGenericArguments()[0] != typeof(string))
            {
                throw new Exception($"dictObject's type should be of type Dictionary<string, T>, current: {dictType.GetType()}");
            }

            Type dataType  = dictType.GetGenericArguments()[1];

            byte[] dictBytes = Serializer.Serialize(dictObject);
            ODTableHeader header = new ODTableHeader()
            {
                TypeFullName = dataType.AssemblyQualifiedName,
            };
            byte[] headerBytes = SerializeMetaData<ODTableHeader>(header);

            ChunkedBytes chunkedBytes = new ChunkedBytes()
            {
                Chunks = new List<ChunkedBytes.Chunk>()
                {
                    new ChunkedBytes.Chunk(headerBytes),
                    new ChunkedBytes.Chunk(dictBytes),
                },
            };
            byte[] finalBytes = ChunkedBytes.ToBytes(chunkedBytes);

            AssetLoader.Dump(tableName.Path, finalBytes, 0, finalBytes.Length);
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Every entry in table</returns>
        public IEnumerable<KeyValuePair<string, T>> GetAll<T>(ODTableName tableName)
            where T : struct
        {
            DBTable? table = EnsureLoad(tableName);
            if (table == null)
            {
                yield break;
            }

            Dictionary<string, T> datas = table.Value.Data as Dictionary<string, T>;
            foreach (var item in datas)
            {
                yield return item;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Every entry in table</returns>
        public IEnumerable<KeyValuePair<string, object>> GetAll(ODTableName tableName)
        {
            DBTable? table = EnsureLoad(tableName);
            if (table == null)
            {
                yield break;
            }

            IDictionary datas = table.Value.Data as IDictionary;
            foreach (object item in datas)
            {
                DictionaryEntry entry = (DictionaryEntry)item;
                yield return new KeyValuePair<string, object>(entry.Key as string, entry.Value);
            }
        }

        /// <summary>
        /// This will not cache, since load class is unkown
        /// </summary>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public IEnumerable<string> GetNames(ODTableName tableName)
        {
            DBTable? table = EnsureLoad(tableName);
            if (table == null)
            {
                yield break;
            }

            IDictionary datas = table.Value.Data as IDictionary;
            foreach (object item in datas.Keys)
            {
                yield return item as string;
            }
        }

        public bool Get<T>(ODTableItemIndexT<T> itemIndex, out T result)
            where T : struct
        {
            DBTable? table = EnsureLoad(itemIndex.TableName);
            if (table == null)
            {
                result = default;
                return false;
            }

            Dictionary<string, T> datas = table.Value.Data as Dictionary<string, T>;
            return datas.TryGetValue(itemIndex.ItemName, out result);
        }

        public object Get(ODTableItemIndex itemIndex)
        {
            DBTable? table = EnsureLoad(itemIndex.TableName);
            if (table == null)
            {
                return null;
            }

            IDictionary datas = table.Value.Data as IDictionary;
            return datas[itemIndex.ItemName];
        }
    }

    public abstract class ODSerializer
    {
        public Dictionary<string, T> DeSerialize<T>(byte[] bytes, int offset, int count)
            where T : struct
        {
            return DeSerialize(typeof(T), bytes, offset, count) as Dictionary<string, T>;
        }

        public virtual byte[] Serialize(object data)
        {
            throw new System.NotImplementedException();
        }

        // Return type is Dictionary<string, T>
        public virtual object DeSerialize(Type type, byte[] bytes, int offset, int count)
        {
            throw new System.NotImplementedException();
        }
    }

    public abstract class ODAssetLoader
    {
        public virtual byte[] Load(AssetPath assetPath)
        {
            throw new System.NotImplementedException();
        }

        public virtual void Dump(AssetPath assetPath, byte[] bytes, int offset, int count)
        {
            throw new System.NotImplementedException();
        }
    }

    public class JsonSerializer : ODSerializer
    {
        protected JsonSerializerSettings m_customSettings;

        public JsonSerializer(JsonSerializerSettings customSettings = null)
        {
            m_customSettings = customSettings;
        }

        public override byte[] Serialize(object data)
        {
            if (data == null)
            {
                return null;
            }

            string jsonStr = JsonConvert.SerializeObject(data, m_customSettings);
            return Encoding.UTF8.GetBytes(jsonStr);
        }

        // Return type is Dictionary<string, type>
        public override object DeSerialize(Type type, byte[] bytes, int offset, int count)
        {
            Type resultTypeTemplate = typeof(Dictionary<,>);
            Type resultType = resultTypeTemplate.MakeGenericType(typeof(string), type);

            if (count == 0)
            {
                return Activator.CreateInstance(resultType);
            }

            string jsonStr = Encoding.UTF8.GetString(bytes, offset, count);
            object result = JsonConvert.DeserializeObject(jsonStr, resultType, m_customSettings);

            return result;
        }
    }

    public class FileAssetLoader : ODAssetLoader
    {
        public string RootDir;
        public bool TouchFile;
        public string Extension = ".txt";

        public FileAssetLoader(string rootDir, bool touchFile = false)
        {
            RootDir = rootDir;
            TouchFile = touchFile;
        }

        public string GetFinalAssetPath(AssetPath assetPath)
        {
            if (RootDir == null || RootDir.Length == 0)
            {
                return assetPath.Path;
            }

            return Path.Combine(RootDir, assetPath.Path);
        }

        protected virtual byte[] LoadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                if (TouchFile)
                {
                    string finalAssetDir = Path.GetDirectoryName(filePath);
                    Directory.CreateDirectory(finalAssetDir);
                    File.Create(filePath);
                }
                return null;
            }

            return File.ReadAllBytes(filePath);
        }

        protected virtual void DumpFile(string filePath, byte[] bytes, int offset, int count)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                fs.Write(bytes, offset, count);
            }
        }

        public override void Dump(AssetPath assetPath, byte[] bytes, int offset, int count)
        {
            string finalAssetPath = GetFinalAssetPath(assetPath);
            DumpFile(finalAssetPath, bytes, offset, count );
        }

        public override byte[] Load(AssetPath assetPath)
        {
            string finalAssetPath = GetFinalAssetPath(assetPath);
            return LoadFile(finalAssetPath);
        }

        private bool FilterFile_Extension(string fileName)
        {
            if (XHelper.StringUtils.IsEmpty(Extension))
            {
                return true;
            }

            string extension = GetFileExtension(fileName);
            if (XHelper.StringUtils.IsEmpty(extension))
            {
                return false;   
            }

            return XHelper.StringUtils.IsSame(Extension, extension, false);
        }

        private bool FilterFile(string fileName)
        {
            if (!FilterFile_Extension(fileName))
            {
                return false;
            }

            return true;
        }

        public static string GetFileExtension(string fileName)
        {
            int dotIndex = fileName.IndexOf('.');
            if (dotIndex != -1)
            {
                return fileName.Substring(dotIndex);
            }

            return null;
        }
    }
}

