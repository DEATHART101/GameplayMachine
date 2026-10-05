using System;
using System.Collections.Generic;
using System.Text;

namespace CommonSerialize
{
    public interface IDataMatrix
    {
        public string GetColName(int col, int layer = 1);
        public string this[int row, int col]
        {
            get;
        }
        public int Columns
        {
            get;
        }
        public int Rows
        {
            get;
        }
        public bool GetIsFinalColLayer(int col, int offsetLayer);

        public bool IsRowEmpty(int row)
        {
            for (int i = 1; i <= Columns; i++)
            {
                if (!string.IsNullOrEmpty(this[row, i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public struct SubDataMatrix : IDataMatrix
    {
        public IDataMatrix FromDataMatrix;

        public int OffsetLayer;

        public int OffsetRow;
        public int OffsetCol;

        public int? SizeRows;
        public int? SizeColumns;

        public string this[int row, int col] => FromDataMatrix[OffsetRow + row, OffsetCol + col];

        public int Columns => SizeColumns ?? FromDataMatrix.Columns;

        public int Rows => SizeRows ?? FromDataMatrix.Rows;


        public string GetColName(int col, int layer = 1)
        {
            return FromDataMatrix.GetColName(OffsetCol + col, OffsetLayer + layer);
        }

        public bool GetIsFinalColLayer(int col, int offsetLayer = 0)
        {
            return FromDataMatrix.GetIsFinalColLayer(OffsetCol + col, OffsetLayer + offsetLayer);
        }
    }
}
