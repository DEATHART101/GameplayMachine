using System;
using System.Collections.Generic;
using System.Text;

namespace GMCore
{
    public class GameplayCacheManager
    {
        #region Define

        public class ObjectCacheData
        {
            public Dictionary<ODFieldName, object> Values;

            public Dictionary<ODFieldName, object> Collections;

            public bool Created;
            public ODClassName ClassName;

            public bool Deleted;
        }

        #endregion

        [System.NonSerialized]
        public Dictionary<GObjectID, ObjectCacheData> CacheDatas = new Dictionary<GObjectID, ObjectCacheData>();

        public void Clear()
        {
            CacheDatas.Clear();
        }

        public void CreateGameplayObject(GObjectID gObjectID, ODClassName clsName)
        {
            ObjectCacheData cacheData;
            if (CacheDatas.TryGetValue(gObjectID, out cacheData))
            {
                throw new Exception($"This GameplayObject was already created ID {gObjectID}");
            }

            cacheData = new ObjectCacheData()
            {
                Created = true,
                ClassName = clsName,
            };
            CacheDatas.Add(gObjectID, cacheData);
        }

        public bool TryGetGameplayObjectValue(GObjectID gObjectID, ODFieldName fieldName, out object outResult)
        {
            outResult = default;

            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                return false;
            }

            CheckDeleted(result);

            if (result.Values == null || !result.Values.TryGetValue(fieldName, out outResult))
            {
                if (result.Created)
                {
                    outResult = default;
                    return true;
                }

                return false;
            }

            return true;
        }

        // Returns true if newly created
        public bool GetOrCreateCollection<T>(GObjectID gObjectID, ODFieldName fieldName, out T outCollection)
            where T : IGMCollection, new()
        {
            ObjectCacheData objectCache;
            if (!CacheDatas.TryGetValue(gObjectID, out objectCache))
            {
                objectCache = new ObjectCacheData();
                CacheDatas.Add(gObjectID, objectCache);
            }

            CheckDeleted(objectCache);

            objectCache.Collections = objectCache.Collections ?? new Dictionary<ODFieldName, object>();

            object outResult;
            if (objectCache.Collections.TryGetValue(fieldName, out outResult))
            {
                outCollection = (T)outResult;
                return false;
            }

            outCollection = new T();
            objectCache.Collections.Add(fieldName, outCollection);

            return true;
        }

        public bool TryGetGameplayObjectDeleted(GObjectID gObjectID, out bool outResult)
        {
            outResult = default;

            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                return false;
            }

            outResult = result.Deleted;
            return true;
        }

        public bool GetGameplayObjectCreated(GObjectID gObjectID)
        {
            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                return false;
            }

            return result.Created;
        }

        public bool TryGetGameplayObjectClass(GObjectID gObjectID, out ODClassName outResult)
        {
            outResult = default;

            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                return false;
            }

            CheckDeleted(result);

            if (!result.Created)
            {
                return false;
            }

            outResult = result.ClassName;
            return true;
        }

        public void SetGameplayObjectValue(GObjectID gObjectID, ODFieldName fieldName, object value)
        {
            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                result = new ObjectCacheData()
                {
                    Values = new Dictionary<ODFieldName, object>()
                    {
                        { fieldName, value },
                    },
                };
                CacheDatas.Add(gObjectID, result);
                return;
            }

            CheckDeleted(result);

            if (result.Values == null)
            {
                result.Values = new Dictionary<ODFieldName, object>();
            }

            result.Values[fieldName] = value;
        }

        public void SetGameplayObjectCollection(GObjectID gObjectID, ODFieldName fieldName, object value)
        {
            if (!(value is ICollectionMemoryObject))
            {
                throw new ArgumentException("The value must be a GameplayMachine collection.", nameof(value));
            }

            if (!CacheDatas.TryGetValue(gObjectID, out ObjectCacheData result))
            {
                result = new ObjectCacheData();
                CacheDatas.Add(gObjectID, result);
            }

            CheckDeleted(result);
            result.Collections = result.Collections ?? new Dictionary<ODFieldName, object>();
            result.Collections[fieldName] = value;
        }

        public void DeleteGameplayObject(GObjectID gObjectID)
        {
            ObjectCacheData result;
            if (!CacheDatas.TryGetValue(gObjectID, out result))
            {
                result = new ObjectCacheData();
                CacheDatas.Add(gObjectID, result);
            }

            CheckDeleted(result);

            result.Deleted = true;
        }

        public void CheckDeleted(ObjectCacheData cacheData)
        {
            if (cacheData.Deleted)
            {
                throw new NotExistException();
            }
        }
    }
}
