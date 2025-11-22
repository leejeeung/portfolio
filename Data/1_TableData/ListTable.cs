using System;
using System.Collections.Generic;
using UnityEngine;

namespace jjevol
{
    public abstract class ListTable<TData> : TableBase where TData : TableData
    {
        public override Type RowType => typeof(TData);

        protected readonly List<TData> _list = new();

        protected ListTable(string tableName) : base(tableName)
        {
        }

        protected abstract TData ParseJson(TSVRow json);

        protected override void InsertData(List<TSVRow> datas)
        {
            _list.Clear();
            if (datas == null) return;

            foreach (var json in datas)
            {
                var data = ParseJson(json);
                _list.Add(data);
            }
        }

        public IReadOnlyList<TData> Datas => _list;

        protected override void _DataClear()
        {
            _list.Clear();
        }
    }
}