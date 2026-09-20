using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class AuthorizationConsumePolicyTests
{
	[RegressionTest]
	private static void NonpersistentDirectPaymentsAndTheirRefundsStillConsumeBaseRequests()
	{
		foreach (bool justPurchased in new[] { true, false })
		{
			AssertEx.True(AuthorizationConsumePolicy.ShouldConsumeBeforeCash(PaymentMode.DirectRadial, false,
				false, justPurchased, requirePrepaidAuthorization: false));
			var use = new FireSupportAuthorizationUse
			{
				Ok = true, ConsumedAuthorization = true,
				PurchasedForBaseRequest = AuthorizationConsumePolicy.PurchasedForBaseRequest(PaymentMode.DirectRadial,
					false, justPurchased, requirePrepaidAuthorization: false)
			};
			AssertEx.True(use.ConsumesBaseRequest);
			AssertEx.True(use.ConsumedAuthorization, "The refundable paid credit must remain attached to the use.");
		}
		AssertEx.True(AuthorizationConsumePolicy.PurchasedForBaseRequest(PaymentMode.Hybrid, false, true, false));
		foreach (PaymentMode mode in Enum.GetValues<PaymentMode>())
		{
			AssertEx.False(AuthorizationConsumePolicy.PurchasedForBaseRequest(mode, false, false, true));
			AssertEx.False(AuthorizationConsumePolicy.PurchasedForBaseRequest(mode, true, true, false));
		}
	}

	[RegressionTest]
	private static void PrepaidPhoneRequestsConsumeCreditsInEveryModeEvenWhenHybridPrefersCash()
	{
		foreach (PaymentMode mode in Enum.GetValues<PaymentMode>())
		foreach (bool persistence in new[] { true, false })
		foreach (bool spendCreditsFirst in new[] { true, false })
		{
			AssertEx.True(AuthorizationConsumePolicy.ShouldConsumeBeforeCash(mode, persistence,
				spendCreditsFirst, consumePurchasedAuthorization: false, requirePrepaidAuthorization: true));
		}
		AssertEx.False(AuthorizationConsumePolicy.ShouldConsumeBeforeCash(PaymentMode.Hybrid, true,
			false, consumePurchasedAuthorization: false, requirePrepaidAuthorization: false));
	}

	[RegressionTest]
	private static void NonpersistentStashCreditRemainsLocalAndIsConsumedOnceWithoutRewritingItsReceipt()
	{
		FireSupportPurchaseResponse purchase = Purchase(nameof(PaymentSource.StashRoubles), 100, "RUB");
		purchase.PurchasedAuthorizationServerBacked = false;
		var local = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
		var server = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 3 };
		AssertEx.True(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out bool requiredServer));
		AssertEx.False(requiredServer);
		AssertEx.True(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out bool consumedServer));
		AssertEx.False(consumedServer);
		AssertEx.False(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out _));
		AssertEx.Equal(3, server[ESupportType.Uav]);
		AssertEx.Equal("StashRoubles", purchase.PaymentSource);
		AssertEx.False(System.Text.Json.JsonSerializer.Serialize(purchase).Contains("PurchasedAuthorizationServerBacked", StringComparison.Ordinal));
	}

	[RegressionTest]
	private static void StashPurchaseConsumesItsServerCreditAndPreservesOlderLocalCredit()
	{
		foreach (string currency in new[] { "RUB", "USD", "EUR", "GP", "BTC" })
		foreach (string source in new[]
		{
			"StashRoubles",
			"PreferCarriedThenStash",
			"PreferStashThenCarried"
		})
		{
			var local = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
			var server = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
			var purchase = Purchase(source, 2, currency);
			AssertEx.True(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out bool requiredServer));
			AssertEx.True(requiredServer);
			AssertEx.True(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out bool consumedServer));
			AssertEx.True(consumedServer);
			AssertEx.Equal(1, local[ESupportType.Uav]);
			AssertEx.Equal(0, server[ESupportType.Uav]);
			AssertEx.False(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out _));
			AssertEx.Equal(1, local[ESupportType.Uav]);
		}
	}

	[RegressionTest]
	private static void CarriedAndFreePurchasesConsumeOnlyTheirLocalCredit()
	{
		foreach (FireSupportPurchaseResponse purchase in new[]
		{
			Purchase("CarriedRoubles", 100, "RUB"),
			Purchase(nameof(PaymentSource.StashRoubles), 0, "BTC")
		})
		{
			var local = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
			var server = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
			AssertEx.True(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out bool requiredServer));
			AssertEx.False(requiredServer);
			AssertEx.True(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out bool consumedServer));
			AssertEx.False(consumedServer);
			AssertEx.Equal(0, local[ESupportType.Uav]);
			AssertEx.Equal(1, server[ESupportType.Uav]);
			AssertEx.False(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, requiredServer, out _));
			AssertEx.Equal(1, server[ESupportType.Uav]);
		}
	}

	[RegressionTest]
	private static void OrdinaryUseStillConsumesLocalBeforeServerAndNeverAnotherService()
	{
		var local = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
		var server = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1, [ESupportType.Strafe] = 2 };
		AssertEx.True(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, null, out bool first));
		AssertEx.False(first);
		AssertEx.Equal(1, server[ESupportType.Uav]);
		AssertEx.True(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, null, out bool second));
		AssertEx.True(second);
		AssertEx.False(AuthorizationConsumePolicy.TryConsume(local, server, ESupportType.Uav, null, out _));
		AssertEx.Equal(2, server[ESupportType.Strafe]);
	}

	[RegressionTest]
	private static void FailedOrUnknownPurchaseCannotChooseACreditSource()
	{
		AssertEx.False(AuthorizationConsumePolicy.TryGetPurchasedSource(null!, out _));
		FireSupportPurchaseResponse purchase = Purchase(nameof(PaymentSource.StashRoubles), 1, "GP");
		purchase.Ok = false;
		AssertEx.False(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out _));
		purchase.Ok = true;
		purchase.AuthorizationGranted = false;
		AssertEx.False(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out _));
		purchase.AuthorizationGranted = true;
		purchase.PaymentSource = "Unknown";
		AssertEx.False(AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out _));
	}

	private static FireSupportPurchaseResponse Purchase(string source, int cost, string currency) => new()
	{
		Ok = true,
		AuthorizationGranted = true,
		SupportType = nameof(ESupportType.Uav),
		PaymentSource = source,
		Cost = cost,
		Currency = currency
	};
}
