namespace ReliableStore
{
    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
    }

    public class Order
    {
        public string Id { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class Customer
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class Payment
    {
        public string Id { get; set; }
        public decimal Amount { get; set; }
    }

    public class Shipment
    {
        public string Id { get; set; }
        public string OrderId { get; set; }
    }
}
