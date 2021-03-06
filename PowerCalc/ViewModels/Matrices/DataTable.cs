﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TAlex.MathCore.ExpressionEvaluation.Trees;
using TAlex.MathCore.ExpressionEvaluation.Trees.Builders;
using TAlex.PowerCalc.Converters;
using TAlex.PowerCalc.Helpers;
using TAlex.PowerCalc.Services;
using TAlex.PowerCalc.ViewModels.Matrices;


namespace TAlex.PowerCalc.ViewModels.Matrices
{
    public class DataTable : IList
    {
        #region Fields

        protected static readonly string CellClipboardFormatName = "DataGridCell";

        protected readonly IExpressionTreeBuilder<Object> ExpressionTreeBuilder;
        protected readonly IClipboardService ClipboardService;
        protected readonly WorksheetMatrixCachedValueConverter CachedValueConverter;

        private List<DataRow> _rows;

        #endregion

        #region Properties

        public int Rows
        {
            get;
            private set;
        }

        public int Columns
        {
            get;
            private set;
        }

        public DataCell this[int row, int col]
        {
            get
            {
                return _rows[row][col];
            }
        }

        #endregion

        #region Constructors

        public DataTable(IExpressionTreeBuilder<Object> expressionTreeBuilder, IClipboardService clipboardService, WorksheetMatrixCachedValueConverter cachedValueConverter)
        {
            ExpressionTreeBuilder = expressionTreeBuilder;
            ClipboardService = clipboardService;
            CachedValueConverter = cachedValueConverter;
            _rows = new List<DataRow>();
        }

        public DataTable(IExpressionTreeBuilder<Object> expressionTreeBuilder, IClipboardService clipboardService, WorksheetMatrixCachedValueConverter cachedValueConverter, int rows, int columns)
            : this(expressionTreeBuilder, clipboardService, cachedValueConverter)
        {
            Initialize(rows, columns);
        }

        #endregion

        #region Methods

        public virtual void Initialize(int rows, int columns)
        {
            Rows = rows;
            Columns = columns;

            _rows.Clear();
            for (int i = 0; i < rows; i++)
            {
                _rows.Add(new DataRow(this, columns) { RowNumber = i + 1 });
            }
        }

        public Expression<Object> BuildTree(string expression)
        {
            return ExpressionTreeBuilder.BuildTree(expression);
        }

        public virtual void Delete(IEnumerable<DataCellInfo> cells)
        {
            IList<DataCell> dataCells = GetDataCells(cells);
            DataCellHelper.EnsureAllArraysEnclosing(dataCells);
            DataCellHelper.GetAllUniqueArrays(dataCells).ForEach(x => x.Clear());

            foreach (DataCell dataCell in dataCells)
            {
                dataCell.Clear();
            }
        }

        public virtual void Copy(IEnumerable<DataCellInfo> cells)
        {
            ClipboardService.SetDataObject(GetCopyText(cells), GetCellsCopyInfo(cells));
        }

        public virtual void Cut(IEnumerable<DataCellInfo> cells)
        {
            DataCellHelper.EnsureAllArraysEnclosing(GetDataCells(cells));
            Copy(cells);
            Delete(cells);
        }

        public virtual void Paste(int row, int column)
        {
            var copyCells = ClipboardService.GetData(typeof(List<DataCellCopyInfo>).FullName) as List<DataCellCopyInfo>;

            if (copyCells != null)
                PasteFromData(row, column, copyCells);
            else
                PasteFromText(row, column);
        }

        public virtual void CheckCircularReferences(string targetCellAddress, DataUnit dataUnit)
        {
            string expression = dataUnit.Expression;
            List<string> cellRangeReferences = A1ReferenceHelper.GetUniqueCellRangeReferences(expression);

            foreach (string reference in cellRangeReferences)
            {
                int row1, column1, row2, column2;
                A1ReferenceHelper.Parse(reference, out column1, out row1, out column2, out row2);

                for (int row = row1; row <= row2; row++)
                {
                    for (int column = column1; column <= column2; column++)
                    {
                        HandleNextCellCircularReferences(row, column, targetCellAddress, dataUnit);
                    }
                }
                expression = expression.Replace(reference, String.Empty);
            }

            IList<string> singleCellReferences = A1ReferenceHelper.GetUniqueSingleCellReferences(expression);

            foreach (string reference in singleCellReferences)
            {
                int row, column;
                A1ReferenceHelper.Parse(reference, out column, out row);
                HandleNextCellCircularReferences(row, column, targetCellAddress, dataUnit);
            }
        }



        private void HandleNextCellCircularReferences(int nextRow, int nextColumn, string targetCellAddress, DataUnit currentDataUnit)
        {
            DataCell nextCell = this[nextRow, nextColumn];

            if ((A1ReferenceHelper.Within(nextRow, nextColumn, targetCellAddress)) || nextCell.CachedValue is CircularReferenceException)
            {
                throw new CircularReferenceException(currentDataUnit);
            }
            if (!String.IsNullOrEmpty(nextCell.Expression)) CheckCircularReferences(targetCellAddress, nextCell);
        }

        #region Helpers

        public IList<DataCell> GetDataCells(IEnumerable<DataCellInfo> cells)
        {
            return cells.Select(x => this[x.RowIndex, x.ColumnIndex]).ToList();
        }

        private List<DataCellCopyInfo> GetCellsCopyInfo(IEnumerable<DataCellInfo> cells)
        {
            int maxArrayIndex = 0;
            Dictionary<DataArray, int> arrayIndexes = new Dictionary<DataArray, int>();
            DataCellInfo topLeft = cells.OrderBy(x => x.ColumnIndex + x.RowIndex).First();
            
            return cells.Select(x =>
            {
                DataCell cell = this[x.RowIndex, x.ColumnIndex];
                int? arrayIndex = null;
                if (cell.Parent != null)
                {
                    int idx;
                    if (!arrayIndexes.TryGetValue(cell.Parent, out idx))
                    {
                        arrayIndexes.Add(cell.Parent, ++maxArrayIndex);
                        idx = maxArrayIndex;
                    }
                    arrayIndex = idx;
                }

                return new DataCellCopyInfo { CellInfo = x.Offset(-topLeft), Expression = cell.Expression, ArrayIndex = arrayIndex };
            }).ToList();
        }

        private string GetCopyText(IEnumerable<DataCellInfo> cells)
        {
            StringBuilder sb = new StringBuilder();
            DataCellInfo topLeft = cells.OrderBy(x => x.ColumnIndex + x.RowIndex).First();
            DataCellInfo buttomRight = cells.OrderBy(x => x.ColumnIndex + x.RowIndex).Last();

            for (int row = topLeft.RowIndex; row <= buttomRight.RowIndex; row++)
            {
                for (int col = topLeft.ColumnIndex; col <= buttomRight.ColumnIndex; col++)
                {
                    sb.Append(CachedValueConverter.ToString(this[row, col].CachedValue));
                    if (col < buttomRight.ColumnIndex) sb.Append("\t");
                }
                if (row < buttomRight.RowIndex) sb.AppendLine();
            }

            return sb.ToString();
        }

        private void PasteFromData(int row, int column, List<DataCellCopyInfo> copyCells)
        {
            List<DataCell> dataCells = copyCells.Select(x => this[row + x.CellInfo.RowIndex, column + x.CellInfo.ColumnIndex]).ToList();
            DataCellHelper.EnsureAllArraysEnclosing(dataCells);

            foreach (DataCellCopyInfo copyCell in copyCells)
            {
                DataCell dataCell = this[row + copyCell.CellInfo.RowIndex, column + copyCell.CellInfo.ColumnIndex];
                dataCell.Parent = null;
                dataCell.Expression = copyCell.Expression;
            }
            foreach (var group in copyCells.Where(x => x.ArrayIndex.HasValue).GroupBy(x => x.ArrayIndex))
            {
                var first = group.First();
                new DataArray(this[row + first.CellInfo.RowIndex, column + first.CellInfo.ColumnIndex], group.Select(x => x.CellInfo.Offset(row, column)).ToList());
            }
        }

        private void PasteFromText(int row, int column)
        {
            var text = ClipboardService.GetText();

            List<List<string>> values = text
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                .Select(l => l.Split('\t').Select(x => x.Trim()).ToList()).ToList();

            int r = 0, c = 0;
            List<DataCell> cells = new List<DataCell>();
            foreach (List<string> line in values)
            {
                line.ForEach(v => { cells.Add(this[row + r, column + c]); c++; });
                c = 0;
                r++;
            }
            DataCellHelper.EnsureAllArraysEnclosing(cells);

            r = 0; c = 0;
            foreach (List<string> line in values)
            {
                foreach (string value in line)
                {
                    DataCell cell = this[row + r, column + c];
                    cell.Parent = null;
                    cell.Expression = value;
                    c++;
                }

                c = 0;
                r++;
            }
        }

        #endregion

        #endregion

        #region IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _rows.GetEnumerator();
        }

        #endregion

        #region ICollection Members

        void ICollection.CopyTo(Array array, int index)
        {
            ((IList)_rows).CopyTo(array, index);
        }

        bool ICollection.IsSynchronized
        {
            get { return ((IList)_rows).IsSynchronized; }
        }

        object ICollection.SyncRoot
        {
            get { return ((IList)_rows).SyncRoot; }
        }

        int ICollection.Count
        {
            get { return _rows.Count; }
        }

        #endregion

        #region IList Members

        int IList.Add(object value)
        {
            return ((IList)_rows).Add(value);
        }

        bool IList.Contains(object value)
        {
            return ((IList)_rows).Contains(value);
        }

        int IList.IndexOf(object value)
        {
            return ((IList)_rows).IndexOf(value);
        }

        void IList.Insert(int index, object value)
        {
            ((IList)_rows).Insert(index, value);
        }

        bool IList.IsFixedSize
        {
            get { return ((IList)_rows).IsFixedSize; }
        }

        bool IList.IsReadOnly
        {
            get { return ((IList)_rows).IsReadOnly; }
        }

        void IList.Remove(object value)
        {
            ((IList)_rows).Remove(value);
        }

        void IList.RemoveAt(int index)
        {
            _rows.RemoveAt(index);
        }

        void IList.Clear()
        {
            _rows.Clear();
        }

        object IList.this[int index]
        {
            get
            {
                return ((IList)_rows)[index];
            }
            set
            {
                ((IList)_rows)[index] = value;
            }
        }

        #endregion

        #region Nested Types

        [Serializable]
        private class DataCellCopyInfo
        {
            public DataCellInfo CellInfo { get; set; }

            public string Expression { get; set; }
            public int? ArrayIndex { get; set; }
        }

        #endregion
    }

    public class CircularReferenceException : Exception
    {
        #region Properties

        public DataUnit DataUnit { get; private set; }

        #endregion

        #region Constructors

        public CircularReferenceException(DataUnit dataUnit)
            : base(Properties.Resources.EXC_CircularReference)
        {
            DataUnit = dataUnit;
        }

        public CircularReferenceException(DataUnit dataUnit, string message)
            : base(message)
        {
            DataUnit = dataUnit;
        }

        #endregion
    }
}
