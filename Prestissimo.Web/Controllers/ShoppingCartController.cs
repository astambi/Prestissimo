﻿namespace Prestissimo.Web.Controllers
{
    using AutoMapper.QueryableExtensions;
    using Data;
    using Data.Models;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Services;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Web.Infrastructure.Extensions;
    using Web.Models.ShoppingCart;

    public class ShoppingCartController : Controller
    {
        private readonly IShoppingCartManager shoppingCartManager;
        private readonly PrestissimoDbContext db;
        private readonly UserManager<User> userManager;

        public ShoppingCartController(
            IShoppingCartManager shoppingCartManager,
            PrestissimoDbContext db,
            UserManager<User> userManager)
        {
            this.shoppingCartManager = shoppingCartManager;
            this.db = db;
            this.userManager = userManager;
        }

        public IActionResult AddToCart(int recordingId, int formatId)
        {
            var recordingFormat = this.db
                .RecordingFormats
                .Where(rf => rf.FormatId == formatId
                        && rf.RecordingId == recordingId)
                .FirstOrDefault();

            if (recordingFormat == null)
            {
                this.TempData.AddErrorMessage(WebConstants.RecordingWithFormatNotFound);
                return this.RedirectToAction(nameof(HomeController.Index), "Home");
            }

            if (recordingFormat.Quantity == 0)
            {
                this.TempData.AddErrorMessage(WebConstants.RecordingWithFormatZeroQuantity);
                return this.RedirectToAction(nameof(HomeController.Index), "Home");
            }

            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();
            this.shoppingCartManager.AddToCart(shoppingCartId, recordingId, formatId);

            return this.RedirectToAction(nameof(Items));
        }

        public IActionResult Items()
        {
            var itemsWithDetails = this.GetShoppingCartItems();

            return View(itemsWithDetails);
        }

        public IActionResult Remove(int recordingId, int formatId)
        {
            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();
            this.shoppingCartManager.RemoveFromCart(shoppingCartId, recordingId, formatId);

            return this.RedirectToAction(nameof(Items));
        }

        public IActionResult IncreaseQuantity(int recordingId, int formatId)
        {
            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();
            this.shoppingCartManager.IncreaseQuantity(shoppingCartId, recordingId, formatId);

            return this.RedirectToAction(nameof(Items));
        }

        public IActionResult DescreaseQuantity(int recordingId, int formatId)
        {
            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();
            this.shoppingCartManager.DecreaseQuantity(shoppingCartId, recordingId, formatId);

            return this.RedirectToAction(nameof(Items));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> FinishOrder(decimal orderTotal)
        {
            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();

            if (orderTotal <= 0)
            {
                this.TempData.AddErrorMessage(WebConstants.ShoppingCartIsEmpty);

                this.shoppingCartManager.Clear(shoppingCartId);

                return this.RedirectToAction(nameof(Items));
            }

            var cartItems = this.GetShoppingCartItems();
            var order = new Order
            {
                UserId = this.userManager.GetUserId(this.User),
                OrderTotal = orderTotal
            };

            var orderItems = new List<OrderItem>();
            foreach (var item in cartItems)
            {
                orderItems.Add(new OrderItem
                {
                    RecordingId = item.RecordingId,
                    RecordingTitle = item.RecordingTitle,
                    FormatId = item.FormatId,
                    FormatName = item.FormatName,
                    Label = this.db
                        .Recordings
                        .Where(r => r.Id == item.RecordingId)
                        .Select(r => r.Label.Name)
                        .FirstOrDefault(),
                    Quantity = item.Quantity,
                    Price = item.Price,
                    Discount = item.Discount
                });

                // update remaining Qty in db
                var recordingFormat = this.db
                    .RecordingFormats
                    .Where(rf => rf.FormatId == item.FormatId
                              && rf.RecordingId == item.RecordingId)
                    .FirstOrDefault();
                recordingFormat.Quantity -= item.Quantity;
                this.db.RecordingFormats.Update(recordingFormat);
            }

            order.OrderItems = orderItems;

            if (orderTotal != order.OrderItems
                            .Sum(i => i.Quantity * i.Price * (1 - (decimal)i.Discount / 100)))
            {
                this.TempData.AddErrorMessage(WebConstants.OrderInvalidData);
                return this.RedirectToAction(nameof(Items));
            }

            await this.db.Orders.AddAsync(order);
            await this.db.SaveChangesAsync();

            this.TempData.AddSuccessMessage(WebConstants.OrderCompleted);

            this.shoppingCartManager.Clear(shoppingCartId);

            return this.RedirectToAction(nameof(Items));
        }

        private List<CartItemViewModel> GetShoppingCartItems()
        {
            var shoppingCartId = this.HttpContext.Session.GetShoppingCartId();

            var items = this.shoppingCartManager.GetItems(shoppingCartId);

            var itemIds = items
                .Select(i => $"{i.RecordingId}:{i.FormatId}")
                .ToList();

            var itemQuantitiesInStore = this.db
                .RecordingFormats
                .Where(rf => itemIds.Contains($"{rf.RecordingId}:{rf.FormatId}"))
                .ToDictionary(i => $"{i.RecordingId}:{i.FormatId}", i => i.Quantity);

            var itemQuantities = items.ToDictionary(i => $"{i.RecordingId}:{i.FormatId}", i => i.Quantity);

            var itemsWithDetails = this.db
                .RecordingFormats
                .Where(rf => itemIds.Contains($"{rf.RecordingId}:{rf.FormatId}")) // todo refactoring 
                .ProjectTo<CartItemViewModel>()
                .ToList();

            itemsWithDetails
                .ForEach(i => i.Quantity = Math.Min(
                    itemQuantities[$"{i.RecordingId}:{i.FormatId}"],
                    itemQuantitiesInStore[$"{i.RecordingId}:{i.FormatId}"]));

            return itemsWithDetails;
        }
    }
}
