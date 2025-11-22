using System.Collections.Generic;
using System.Threading.Tasks;

namespace jjevol.Data
{
    public static class TableRepository
    {
        private static readonly List<TableBase> _allTables = new();

        public static void Register(TableBase table)
        {
            _allTables.Add(table);
        }

        public static void LoadAll()
        {
            foreach (var table in _allTables)
                table.LoadDatas();
        }

        public static async Task LoadAllAsync()
        {
            foreach (var table in _allTables)
                await table.LoadDatasAsync();
        }
    }
}
