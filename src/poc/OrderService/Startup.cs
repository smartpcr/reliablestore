//-------------------------------------------------------------------------------
// <copyright file="Startup.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace OrderService
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Common.Persistence;
    using Common.Tx;
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            
            // Register FileStore for Order
            services.AddSingleton<FileStore<Order>>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStore<Order>>>();
                return new FileStore<Order>("data/orders.json", logger);
            });
            
            // Register FileStore for Product (shared catalog)
            services.AddSingleton<FileStore<Product>>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStore<Product>>>();
                return new FileStore<Product>("data/products.json", logger);
            });
            
            // Register FileStore for Payment (cross-service coordination)
            services.AddSingleton<FileStore<Payment>>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStore<Payment>>>();
                return new FileStore<Payment>("data/payments.json", logger);
            });
            
            // Register FileStore for Shipment (cross-service coordination)
            services.AddSingleton<FileStore<Shipment>>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStore<Shipment>>>();
                return new FileStore<Shipment>("data/shipments.json", logger);
            });
            
            // Register transaction services
            services.AddTransactionSupport();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
