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

	[RegressionTest]
	private static void RefundedCashRequestsKeepTheirBudgetCostAcrossRepeatedRetries()
	{
		foreach (PaymentMode mode in new[] { PaymentMode.DirectRadial, PaymentMode.Hybrid })
		{
			var local = new Dictionary<ESupportType, int> { [ESupportType.Uav] = 1 };
			var server = new Dictionary<ESupportType, int>();
			var refunds = new Dictionary<ESupportType, int>();
			int budget = 1;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				AssertEx.True(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
					ESupportType.Uav, false, out bool serverBacked, out bool refundedBaseRequest));
				AssertEx.False(serverBacked);
				var use = new FireSupportAuthorizationUse
				{
					Ok = true, ConsumedAuthorization = true,
					PurchasedForBaseRequest = refundedBaseRequest || AuthorizationConsumePolicy.PurchasedForBaseRequest(
						mode, false, consumePurchasedAuthorization: attempt == 0, requirePrepaidAuthorization: false)
				};
				AssertEx.True(use.ConsumesBaseRequest, $"{mode} attempt {attempt} must spend the same base request.");
				budget--;
				if (attempt == 2) continue; // The third dispatch is accepted.
				AuthorizationConsumePolicy.RefundLocal(local, refunds, ESupportType.Uav, use.PurchasedForBaseRequest);
				budget++;
				AssertEx.Equal(0, local[ESupportType.Uav]);
				AssertEx.Equal(1, refunds[ESupportType.Uav]);
			}
			AssertEx.Equal(0, budget);
			AssertEx.False(AuthorizationConsumePolicy.HasRequestBudget(mode, budget, false, false));
			AssertEx.False(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
				ESupportType.Uav, false, out _, out _));
		}
	}

	[RegressionTest]
	private static void RealPrepaidCreditsBypassBudgetWithoutLaunderingCashRefunds()
	{
		var local = new Dictionary<ESupportType, int>();
		var server = new Dictionary<ESupportType, int>();
		var refunds = new Dictionary<ESupportType, int>();
		AuthorizationConsumePolicy.RefundLocal(local, refunds, ESupportType.Extract, purchasedForBaseRequest: true);
		AuthorizationConsumePolicy.RefundLocal(local, refunds, ESupportType.Extract, purchasedForBaseRequest: false);
		foreach (PaymentMode mode in new[] { PaymentMode.Hybrid, PaymentMode.PhoneAuthorizations })
		{
			AssertEx.True(AuthorizationConsumePolicy.HasRequestBudget(mode, 0, true, true));
			AssertEx.False(AuthorizationConsumePolicy.HasRequestBudget(mode, 0, true, false),
				"A cash refund alone cannot bypass an exhausted base budget.");
			AssertEx.True(AuthorizationConsumePolicy.HasRequestBudget(mode, 1, true, false));
		}
		AssertEx.True(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
			ESupportType.Extract, null, out bool backed, out bool baseRequest));
		AssertEx.False(backed);
		AssertEx.False(baseRequest, "The genuine prepaid credit must retain its budget bypass.");
		AssertEx.Equal(1, refunds[ESupportType.Extract]);
		AssertEx.True(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
			ESupportType.Extract, null, out backed, out baseRequest));
		AssertEx.False(backed);
		AssertEx.True(baseRequest);
	}

	[RegressionTest]
	private static void ServerPurchaseConsumptionDoesNotTakeARefundedLocalPayment()
	{
		var local = new Dictionary<ESupportType, int>();
		var server = new Dictionary<ESupportType, int> { [ESupportType.Strafe] = 1 };
		var refunds = new Dictionary<ESupportType, int> { [ESupportType.Strafe] = 1 };
		AssertEx.True(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
			ESupportType.Strafe, true, out bool backed, out bool baseRequest));
		AssertEx.True(backed);
		AssertEx.False(baseRequest);
		AssertEx.Equal(1, refunds[ESupportType.Strafe]);
		AssertEx.False(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
			ESupportType.Strafe, true, out _, out _));
		server[ESupportType.Strafe] = 1;
		AssertEx.True(AuthorizationConsumePolicy.TryConsumeForDeployment(local, server, refunds,
			ESupportType.Strafe, null, out backed, out baseRequest));
		AssertEx.True(backed, "A genuine server credit must also precede cash refunds when bypassing the base budget.");
		AssertEx.False(baseRequest);
		AssertEx.Equal(1, refunds[ESupportType.Strafe]);
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
