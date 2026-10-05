using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace CommonSerialize
{
    [System.Serializable]
    public class ListDataProvider : IListDataProvider
    {
        public List<List<KeyValuePair<string, string>>> Datas;

        public IEnumerable<IEnumerable<KeyValuePair<string, string>>> Provide()
        {
            if (Datas == null)
            {
                yield break;
            }

            foreach (var data in Datas) 
            {
                yield return data;
            }
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, this);
                return ms.ToArray();
            }
        }

        public static ListDataProvider FromBytes(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (ListDataProvider)formatter.Deserialize(ms);
            }
        }
    }

    [System.Serializable]
    public class MapDataProvider : IMapDataProvider
    {
        public Dictionary<string, List<KeyValuePair<string, string>>> Datas;

        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<string, string>>>> Provide()
        {
            if (Datas == null)
            {
                yield break;
            }

            foreach (var data in Datas)
            {
                yield return new KeyValuePair<string, IEnumerable<KeyValuePair<string, string>>>(data.Key, data.Value);
            }
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, this);
                return ms.ToArray();
            }
        }

        public static MapDataProvider FromBytes(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (MapDataProvider)formatter.Deserialize(ms);
            }
        }
    }

    

    [System.Serializable]
    public class CellDataProvider : IDataMatrix
    {
        #region Define

        [System.Serializable]
        public struct Row<T>
        {
            public int Line;
            public List<T> Contents;
        }

        #endregion

        private List<Row<string>> m_rows;
        private int m_width;
        private int m_height;
        private List<string[]> m_colNames;

        public int Width { get => m_width; }
        public int Height { get => m_height; }

        public int Columns => Width;

        public int Rows => Height;

        public string this[int row, int col] => GetItem(col - 1, row - 1);

        public CellDataProvider(int width, int height, List<string[]> colNames)
        {
            this.m_width = width;
            this.m_height = height;
            m_rows = new List<Row<string>>();
            m_colNames = colNames;
        }

        public void AddLast(List<string> row)
        {
            Row<string> final;
            if (m_rows.Count == 0)
            {
                final = new Row<string>()
                {
                    Line = 0,
                    Contents = row,
                };
            }
            else
            {
                final = m_rows[m_rows.Count - 1];
                final.Line++;
                final.Contents = row;
            }

            m_rows.Add(final);
        }

        public void AddRow(Row<string> row)
        {
            if (row.Line < 0 || row.Line >= m_height)
            {
                throw new ArgumentOutOfRangeException(nameof(row.Line), "Line number out of range.");
            }

            // Insert row maintaining the sorted order by Line
            int index = m_rows.BinarySearch(row, Comparer<Row<string>>.Create((r1, r2) => r1.Line.CompareTo(r2.Line)));
            if (index < 0)
            {
                m_rows.Insert(~index, row); // ~index gives the index where it should be inserted
            }
        }

        private string GetItem(int x, int y)
        {
            if (x < 0 || x >= m_width || y < 0 || y >= m_height)
                throw new ArgumentOutOfRangeException("Coordinates are out of bounds.");

            // Perform binary search to find the appropriate row
            Row<string> searchRow = new Row<string>();
            {
                searchRow.Line = y;
            }
            int index = m_rows.BinarySearch(searchRow, Comparer<Row<string>>.Create((r1, r2) => r1.Line.CompareTo(r2.Line)));

            if (index >= 0) // Row found
            {
                Row<string> foundRow = m_rows[index];
                if (x < foundRow.Contents.Count)
                {
                    return foundRow.Contents[x];
                }
            }
            // If row not found or index is out of bounds, return default
            return "";
        }

        public string GetColName(int col, int layer = 1)
        {
            return m_colNames[col - 1][layer - 2];
        }

        public bool GetIsFinalColLayer(int col, int offsetLayer)
        {
            return offsetLayer == m_colNames[col - 1].Length;
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, this);
                return ms.ToArray();
            }
        }

        public static CellDataProvider FromBytes(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (CellDataProvider)formatter.Deserialize(ms);
            }
        }
    }
}
