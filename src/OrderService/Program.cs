using System;
using Microsoft.Owin.Hosting;

namespace OrderService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://*:9002"))
            {
                Console.WriteLine("OrderService running...");
                Console.ReadLine();
            }
        }
    }
}
