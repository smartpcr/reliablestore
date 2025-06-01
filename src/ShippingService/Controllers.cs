using System.Web.Http;
using ReliableStore;

namespace ShippingService
{
    [RoutePrefix("api/shipping")]
    public class ShippingController : ApiController
    {
        private static readonly FileRepository<Shipment> _repo = new FileRepository<Shipment>("shipments.json");

        [HttpPost, Route("ship")]
        public IHttpActionResult Ship(Shipment shipment)
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
