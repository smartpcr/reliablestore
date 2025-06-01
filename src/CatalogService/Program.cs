using System;
using Microsoft.Owin.Hosting;

namespace CatalogService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://*:9001"))
            {
                Console.WriteLine("CatalogService running...");
                Console.ReadLine();
            }
        }
    }
}
