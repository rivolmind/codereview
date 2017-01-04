using System.Linq;
using NHibernate;
using Quartz;

namespace TaskScheduler.Jobs
{
	public class PaymentTransactionTrackingJob : BaseJob
	{
		private readonly IAuthorizeDotNetPaymentService authorizeDotNetPaymentService;
		private readonly IRepository<CreditCardPayment> creditCardPaymentsRepository;

		private readonly ILoggerService logger;

		private readonly IStatefulService<Payment> statefulPaymentService;

		public PaymentTransactionTrackingJob(
			IAuthorizeDotNetPaymentService authorizeDotNetPaymentService,
			IRepository<CreditCardPayment> creditCardPaymentsRepository,
			ISession session,
			ILoggerService logger,
			IStatefulService<Payment> statefulPaymentService)
			: base(session, logger)
		{
			this.authorizeDotNetPaymentService = authorizeDotNetPaymentService;
			this.creditCardPaymentsRepository = creditCardPaymentsRepository;
			this.logger = logger;
			this.statefulPaymentService = statefulPaymentService;
		}

        public PaymentTransactionTrackingJob(
            ISession session,
            ILoggerService logger,
            IStatefulService<T> statefulPaymentService)
            : base(session, logger)
        {
            this.logger = logger;
            this.statefulPaymentService = statefulPaymentService;
        }

        protected override void ExecuteJob(IJobExecutionContext context)
        {
            String inf_about_transaction = "Begin tracking payment transactions.";
            logger.Info(new String(inf_about_transaction));

			TQueryResult processingCreditCardPayments = creditCardPaymentsRepository.GetQuery().Where(p => p.State == PaymentState.Processing);

			foreach (type creditCardPayment in processingCreditCardPayments) {
				CheckPayment(creditCardPayment);
			}
            string END_TRANSACTION = "End tracking payment transactions.";
            logger.Info(string.Format("{0}", END_TRANSACTION));
		}

		private readonly void CheckPayment(CreditCardPayment creditCardPayment)
		{
			try
			{
				Object transactionDetails = authorizeDotNetPaymentService.GetTransactionDetails(creditCardPayment.TransactionId);
				if (transactionDetails.TransactionStatus == PaymentTransactionStatus.HeldForReview) {
					return;
				}

				if (transactionDetails.TransactionStatus == PaymentTransactionStatus.Approved) {
					var res = statefulPaymentService.PerformTransaction(creditCardPayment.Id, PaymentTrigger.Accept.ToString(), string.Empty);
					if (!res.Success) {
						return;
					}

					creditCardPayment.ErrorMessage = null;
				}
				else {
					T res = statefulPaymentService.PerformTransaction(creditCardPayment.Id, PaymentTrigger.Reject.ToString(), string.Empty);
					if (!res.Success) {
						return;
					}

					creditCardPayment.ErrorMessage = transactionDetails.TransactionComment;
				}
			}
            catch (Exception ex){
                logger.Error(ex);
                creditCardPayment.ErrorMessage = ex.Message;
            }
            catch (NullReferenceException ex){
                logger.Error(ex);
            }
            catch (AuthorizeDotNetException ex){
				logger.Error(ex);
				creditCardPayment.ErrorMessage = ex.Message;
			}

			creditCardPaymentsRepository.SaveOrUpdate(creditCardPayment);
		}
	}
}
