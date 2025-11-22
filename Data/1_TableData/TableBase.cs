//#define LOAD_STATIC
#define LOAD_LOCAL_RESOURCE

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace jjevol
{
    public interface ITable
    {
        Type RowType { get; }
        string TableName { get; }
    }

    public abstract class TableBase : ITable
    {
        public abstract Type RowType { get; }

        protected string _tableName;

        public string TableName => _tableName;

        protected TableBase(string tableName)
        {
            _tableName = tableName;
        }

        public virtual void LoadDatas()
        {
            _ClearAndLoadData(TableName);
        }

        public virtual async Task LoadDatasAsync()
        {
            var tableDatas = await LoadTableDataAsync(TableName);
            _DataClear();
            InsertData(tableDatas);
        }

        protected void _ClearAndLoadData(string _tableName)
        {
            _DataClear();
            var tableDatas = LoadTableData(_tableName);
            InsertData(tableDatas);
        }

        protected List<TSVRow> LoadTableData(string _tableName)
        {
#if LOAD_STATIC
            return TableUtil.LoadStaticTable(_tableName, true);
#elif LOAD_LOCAL_RESOURCE
            return TableUtil.LoadLocalTable(_tableName, true);
#else
            return new List<TSVRow>();
#endif
        }

        protected async Task<List<TSVRow>> LoadTableDataAsync(string _tableName)
        {
#if LOAD_STATIC
            return await TableUtil.LoadStaticTableAsync(_tableName, true);
#elif LOAD_LOCAL_RESOURCE
            return await TableUtil.LoadLocalTableAsync(_tableName, true); // 비동기 필요시 확장 가능
#else
            return await Task.FromResult(new List<TSVRow>());
#endif
        }

        protected abstract void _DataClear();
        protected abstract void InsertData(List<TSVRow> datas);
    }
}
