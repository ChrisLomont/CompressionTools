/*
MIT License

Copyright (c) 2016 Chris Lomont

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lomont.Compression
{
    /// <summary>
    /// A simple class to manipulate markdown tables
    /// </summary>
    public class MarkdownTable
    {

        public class Cell
        {
            private string text = "";

            public string Text
            {
                get
                {
                    if (!Marked) return text;
                    return "**" + text + "**";
                }
                set { text = value; }
            }
            public bool Marked { get; set; }
        }

        // list of rows, each a list of cells
        readonly List<List<Cell>> rows = new List<List<Cell>>();

        public int Height => rows.Count;
        public int Width => rows.Any() ? rows[0].Count : 0;

        public int AddColumn(string header = "")
        {
            foreach (var row in rows)
                row.Add(new Cell());
            if (!rows.Any())
                rows.Add(new List<Cell> {new Cell()});
            rows[0][rows[0].Count-1].Text = header;
            return rows[0].Count - 1;
        }

        public int AddRow(string text="")
        {
            var row = new List<Cell>();
            if (rows.Count > 0)
            {
                for (var i =0; i < rows[0].Count; ++i)
                    row.Add(new Cell());
            }
            else
                row.Add(new Cell());
            row[0].Text = text;
            rows.Add(row);
            return rows.Count-1;
        }

        public void SetCell(int row, int column, string text)
        {
            rows[row][column].Text = text;
        }

        public int ToInt(int row, int column)
        {
            int v;
            if (Int32.TryParse(rows[row][column].Text, out v))
                return v;
            return 0;
        }


        public int SumCol(int row1, int row2, int column)
        {
            if (row2 < row1)
            {
                var temp = row1;
                row1 = row2;
                row2 = temp;
            }
            var sum = 0;
            for (var  r = row1; r <= row2; ++r )
                    sum += ToInt(r, column);
            SetCell(row2+1, column, sum.ToString());
            return sum;
        }

        public void MarkCell(int row, int column, bool flag)
        {
            rows[row][column].Marked = flag;
        }




        public void Write(TextWriter output)
        { 
            // compute spacing
            var w = Width;
            var h = Height;
            var colSizes = new int[w];
            for (var i = 0; i < w; ++i)
                for (var j = 0; j < h; ++j)
                    colSizes[i] = Math.Max(colSizes[i], rows[j][i].Text.Length);

            var lineSplitter = '|' + colSizes.Select(c => new String('-', c + 2)).Aggregate((a, b) => a + '|' + b) + '|';

            // write table
            for (var j = 0; j < h; ++j)
            {
                output.Write(" | ");
                for (var i = 0; i < w; ++i)
                {
                    var format = $"{{0,{colSizes[i]}}}";
                    output.Write(format, rows[j][i].Text);
                    output.Write(" | ");
                }
                output.WriteLine();
                if (j == 0)
                    output.WriteLine(' ' + lineSplitter);
            }
            output.WriteLine();
            output.WriteLine();
        }

    }
}
