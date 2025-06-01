using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ReliableStore
{
    public class FileRepository<T> where T : class
    {
        private readonly string _filePath;

        public FileRepository(string filePath)
        {
            _filePath = filePath;
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(new List<T>()));
            }
        }

        private List<T> Load()
        {
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<List<T>>(json);
        }

        private void Save(List<T> items)
        {
            var json = JsonConvert.SerializeObject(items, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        public void Add(T item, ITransaction tx)
        {
            tx.Enlist(() =>
            {
                var items = Load();
                items.Add(item);
                Save(items);
            },
            () =>
            {
                var items = Load();
                items.Remove(item);
                Save(items);
            });
        }
    }
}
