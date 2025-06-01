using System;
using Microsoft.Owin.Hosting;

namespace CustomerService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://*:9003"))
            {
                Console.WriteLine("CustomerService running...");
                Console.ReadLine();
            }
        }
    }
}
