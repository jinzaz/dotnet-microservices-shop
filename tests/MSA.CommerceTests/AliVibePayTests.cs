using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Client.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.PaymentCore;
using System.Security.Cryptography;

namespace MSA.CommerceTests;

[TestClass]
public sealed class AliVibePayTests
{
    [TestMethod]
    public void IsConfigured_RequiresAppIdAndBothKeys()
    {
        Assert.IsFalse(new PaylinksOptions.AlipaySettings().IsConfigured);
        Assert.IsFalse(new PaylinksOptions.AlipaySettings { AppId = "1", AppPrivateKey = "k" }.IsConfigured);
        Assert.IsTrue(new PaylinksOptions.AlipaySettings { AppId = "1", AppPrivateKey = "k", AlipayPublicKey = "p" }.IsConfigured);
    }

    [TestMethod]
    public void IsPaidNotification_RejectsRefundEventsAndUnpaidStates()
    {
        Assert.IsTrue(AliVibePay.IsPaidNotification(Params(("trade_status", "TRADE_SUCCESS"))));
        Assert.IsTrue(AliVibePay.IsPaidNotification(Params(("trade_status", "TRADE_FINISHED"))));
        Assert.IsFalse(AliVibePay.IsPaidNotification(Params(("trade_status", "WAIT_BUYER_PAY"))));
        Assert.IsFalse(AliVibePay.IsPaidNotification(Params(("trade_status", "TRADE_SUCCESS"), ("refund_fee", "1.00"))));
        Assert.IsFalse(AliVibePay.IsPaidNotification(Params(("trade_status", "TRADE_SUCCESS"), ("out_biz_no", "r1"))));
    }

    [TestMethod]
    public void BelongsToMerchant_ChecksAppIdAndConfiguredSeller()
    {
        var settings = new PaylinksOptions.AlipaySettings { AppId = "app", SellerId = "2088" };
        Assert.IsTrue(AliVibePay.BelongsToMerchant(Params(("app_id", "app"), ("seller_id", "2088")), settings));
        Assert.IsFalse(AliVibePay.BelongsToMerchant(Params(("app_id", "other"), ("seller_id", "2088")), settings));
        Assert.IsFalse(AliVibePay.BelongsToMerchant(Params(("app_id", "app"), ("seller_id", "2099")), settings));
        settings.SellerId = "";
        Assert.IsTrue(AliVibePay.BelongsToMerchant(Params(("app_id", "app")), settings));
    }

    [TestMethod]
    public async Task CreateNativePayment_ReturnsSignedPagePayFormWithoutLoopbackNotifyUrl()
    {
        var services = new ServiceCollection();
        services.AddAlipayClient();
        using var provider = services.BuildServiceProvider();
        using var rsa = RSA.Create(2048);
        var options = Options.Create(new PaylinksOptions
        {
            AliVibePay = new()
            {
                ServerUrl = "https://openapi-sandbox.dl.alipaydev.com",
                AppId = "2021000000000000",
                AppPrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey()),
                AlipayPublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())
            }
        });
        var gateway = new AliVibePayGateway(provider.GetRequiredService<IAlipayClient>(), options);

        var result = await gateway.CreateNativePaymentAsync(
            new CreatePaymentRequest(
                "a1b2", 12.3m, "CNY", "Order a1b2", "https://localhost:10005/payment/callbacks/alipay",
                new DateTimeOffset(2026, 10, 10, 4, 0, 0, TimeSpan.Zero), "http://localhost:5173/payment/a1b2"),
            CancellationToken.None);

        Assert.AreEqual(PaymentChannel.Alipay, result.Channel);
        Assert.StartsWith("<form", result.PaymentUrl);
        Assert.Contains("alipay.trade.page.pay", result.PaymentUrl);
        Assert.Contains("\"product_code\":\"FAST_INSTANT_TRADE_PAY\"", result.PaymentUrl);
        Assert.Contains("\"total_amount\":\"12.30\"", result.PaymentUrl);
        Assert.Contains("\"time_expire\":\"2026-10-10 12:00:00\"", result.PaymentUrl);
        Assert.Contains("http://localhost:5173/payment/a1b2", result.PaymentUrl);
        Assert.DoesNotContain("notify_url", result.PaymentUrl, "loopback notify_url must not be sent");
    }

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value);
}
