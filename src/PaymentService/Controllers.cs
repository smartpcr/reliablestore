using System.Web.Http;
using ReliableStore;

namespace PaymentService
{
    [RoutePrefix("api/payment")]
    public class PaymentController : ApiController
    {
        private static readonly FileRepository<Payment> _repo = new FileRepository<Payment>("payments.json");

        [HttpPost, Route("charge")]
        public IHttpActionResult Charge(Payment payment)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(payment, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}
