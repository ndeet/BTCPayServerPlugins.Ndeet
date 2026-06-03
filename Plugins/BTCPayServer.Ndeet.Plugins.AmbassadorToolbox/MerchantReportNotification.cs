using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class MerchantReportNotification : BaseNotification
{
    private const string Type = "merchantreport";

    public string ReportId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    public override string Identifier => Type;
    public override string NotificationType => Type;

    public MerchantReportNotification()
    {
    }

    public MerchantReportNotification(MerchantReport report)
    {
        ReportId = report.Id;
        StoreId = report.StoreId;
        StoreName = report.StoreName;
        InvoiceId = report.InvoiceId;
        Reason = report.Reason;
    }

    public class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer stringLocalizer)
        : NotificationHandler<MerchantReportNotification>
    {
        public override string NotificationType => Type;

        public override (string identifier, string name)[] Meta =>
        [
            (Type, stringLocalizer["Merchant reports"])
        ];

        protected override void FillViewModel(MerchantReportNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.StoreId = notification.StoreId;
            vm.Body = stringLocalizer[
                "Merchant report submitted for {0}: {1}",
                string.IsNullOrWhiteSpace(notification.StoreName) ? notification.StoreId : notification.StoreName,
                notification.Reason];
            vm.ActionLink = linkGenerator.GetPathByAction(
                nameof(AmbassadorToolboxController.Index),
                "AmbassadorToolbox",
                values: null,
                pathBase: options.RootPath);
        }
    }
}
