using System;
using System.Linq;
using Nop.Core;
using Nop.Core.Data;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Services.Events;

namespace Nop.Services.Orders
{
    /// <summary>
    /// Reward point service
    /// </summary>
    public partial class RewardPointService : IRewardPointService
    {
        #region Fields

        private readonly IRepository<RewardPointsHistory> _rphRepository;
        private readonly RewardPointsSettings _rewardPointsSettings;
        private readonly IStoreContext _storeContext;
        private readonly IEventPublisher _eventPublisher;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="rphRepository">RewardPointsHistory repository</param>
        /// <param name="rewardPointsSettings">Reward points settings</param>
        /// <param name="storeContext">Store context</param>
        /// <param name="eventPublisher">Event published</param>
        public RewardPointService(IRepository<RewardPointsHistory> rphRepository,
            RewardPointsSettings rewardPointsSettings,
            IStoreContext storeContext,
            IEventPublisher eventPublisher)
        {
            this._rphRepository = rphRepository;
            this._rewardPointsSettings = rewardPointsSettings;
            this._storeContext = storeContext;
            this._eventPublisher = eventPublisher;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Update reward points balance if necessary
        /// </summary>
        /// <param name="query">Input query</param>
        /// <returns>Output query</returns>
        protected IQueryable<RewardPointsHistory> UpdateRewardPointsBalance(IQueryable<RewardPointsHistory> query)
        {
            var rewardPoints = query.ToList();
            for (var i = rewardPoints.Count - 1; i >= 0; i--)
            {
                if (!rewardPoints[i].PointsBalance.HasValue && rewardPoints[i].CreatedOnUtc < DateTime.UtcNow)
                {
                    rewardPoints[i].PointsBalance = rewardPoints[i].Points + (rewardPoints.Count > 1 ? rewardPoints[i + 1].PointsBalance : 0);
                    UpdateRewardPointsHistoryEntry(rewardPoints[i]);
                }
            }

            return rewardPoints.AsQueryable();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Load reward point history records
        /// </summary>
        /// <param name="customerId">Customer identifier; 0 to load all records</param>
        /// <param name="showHidden">A value indicating whether to show hidden records (filter by current store if possible)</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Reward point history records</returns>
        public virtual IPagedList<RewardPointsHistory> GetRewardPointsHistory(int customerId = 0, bool showHidden = false,
            int pageIndex = 0, int pageSize = int.MaxValue)
        {
            var query = _rphRepository.Table;
            if (customerId > 0)
                query = query.Where(rph => rph.CustomerId == customerId);
            if (!showHidden)
            {
                //show only the points that already accrued
                query = query.Where(rph => rph.CreatedOnUtc < DateTime.UtcNow);

                //filter by store
                if (!_rewardPointsSettings.PointsAccumulatedForAllStores)
                    query = query.Where(rph => rph.StoreId == _storeContext.CurrentStore.Id);
            }

            query = query.OrderByDescending(rph => rph.CreatedOnUtc).ThenByDescending(rph => rph.Id);

            query = UpdateRewardPointsBalance(query);

            var records = new PagedList<RewardPointsHistory>(query, pageIndex, pageSize);
            return records;
        }

        /// <summary>
        /// Add reward points history record
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="points">Number of points to add</param>
        /// <param name="storeId">Store identifier</param>
        /// <param name="message">Message</param>
        /// <param name="usedWithOrder">The order for which points were redeemed (spent) as a payment</param>
        /// <param name="usedAmount">Used amount</param>
        /// <param name="accrualDate">Date and time of accrual reward points; pass null to immediately accruing</param>
        public virtual void AddRewardPointsHistoryEntry(Customer customer,
            int points, int storeId, string message = "",
            Order usedWithOrder = null, decimal usedAmount = 0M, DateTime? accrualDate = null)
        {
            if (customer == null)
                throw new ArgumentNullException("customer");

            if (storeId <= 0)
                throw new ArgumentException("Store ID should be valid");

            var rph = new RewardPointsHistory
            {
                Customer = customer,
                StoreId = storeId,
                UsedWithOrder = usedWithOrder,
                Points = points,
                PointsBalance = accrualDate.HasValue ? null : (int?)(GetRewardPointsBalance(customer.Id, storeId) + points),
                UsedAmount = usedAmount,
                Message = message,
                CreatedOnUtc = accrualDate ?? DateTime.UtcNow
            };

            _rphRepository.Insert(rph);

            //event notification
            _eventPublisher.EntityInserted(rph);
        }

        /// <summary>
        /// Gets reward points balance
        /// </summary>
        /// <param name="customerId">Customer identifier</param>
        /// <param name="storeId">Store identifier; pass </param>
        /// <returns>Balance</returns>
        public virtual int GetRewardPointsBalance(int customerId, int storeId)
        {
            var query = _rphRepository.Table;
            if (customerId > 0)
                query = query.Where(rph => rph.CustomerId == customerId);
            if (!_rewardPointsSettings.PointsAccumulatedForAllStores)
                query = query.Where(rph => rph.StoreId == storeId);

            //show only the points that already accrued
            query = query.Where(rph => rph.CreatedOnUtc < DateTime.UtcNow);

            query = query.OrderByDescending(rph => rph.CreatedOnUtc).ThenByDescending(rph => rph.Id);

            query = UpdateRewardPointsBalance(query);

            var lastRph = query.FirstOrDefault();
            return lastRph != null && lastRph.PointsBalance.HasValue ? lastRph.PointsBalance.Value : 0;
        }

        /// <summary>
        /// Updates the reward point history entry
        /// </summary>
        /// <param name="rewardPointsHistory">Reward point history entry</param>
        public virtual void UpdateRewardPointsHistoryEntry(RewardPointsHistory rewardPointsHistory)
        {
            if (rewardPointsHistory == null)
                throw new ArgumentNullException("rewardPointsHistory");

            _rphRepository.Update(rewardPointsHistory);

            //event notification
            _eventPublisher.EntityUpdated(rewardPointsHistory);
        }

        #endregion
    }
}
