using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace ShippingService
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShippingController : ControllerBase
    {
        private static readonly FileRepository<Shipment> _repo = new FileRepository<Shipment>("shipments.json");

        [HttpPost("ship")]
        public IActionResult Ship([FromBody] Shipment shipment)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(shipment, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}
