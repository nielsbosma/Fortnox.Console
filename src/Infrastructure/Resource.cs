namespace Fortnox.Console.Infrastructure;

[Flags]
public enum Ops
{
    None = 0,
    List = 1,
    Get = 2,
    Create = 4,
    Update = 8,
    Delete = 16,
    Read = List | Get,
    Write = Create | Update,
    All = Read | Write | Delete
}

/// <summary>
/// A follow-up action on one resource, e.g. <c>invoices bookkeep 123</c> →
/// <c>PUT invoices/123/bookkeep</c>. Irreversible ones demand <c>--yes</c>.
/// </summary>
public sealed record ResourceAction(string Name, string Method, string Segment, string Description, bool Irreversible = false);

/// <summary>
/// One Fortnox collection. Fortnox wraps every payload in a key named after the resource:
/// lists in the plural (<c>{"Customers":[...]}</c>), single objects in the singular
/// (<c>{"Customer":{...}}</c>), and writes expect the same singular wrapper.
/// </summary>
public sealed record Resource(
    string Command,
    string Path,
    string Plural,
    string Singular,
    string IdField,
    string Scope,
    string Description,
    Ops Ops,
    string[]? Filters = null,
    ResourceAction[]? Actions = null,
    string? Note = null,
    bool Pdf = false)
{
    public ResourceAction[] AllActions => Actions ?? [];
}

public static class ResourceCatalog
{
    public static readonly Resource[] All =
    [
        new("customers", "customers", "Customers", "Customer", "CustomerNumber", "customer",
            "Customers", Ops.All, ["active", "inactive"]),

        new("suppliers", "suppliers", "Suppliers", "Supplier", "SupplierNumber", "supplier",
            "Suppliers", Ops.All, ["active", "inactive"]),

        new("articles", "articles", "Articles", "Article", "ArticleNumber", "article",
            "Articles (products and services)", Ops.All, ["active", "inactive"]),

        new("invoices", "invoices", "Invoices", "Invoice", "DocumentNumber", "invoice",
            "Customer invoices", Ops.Read | Ops.Write,
            ["cancelled", "fullypaid", "unpaid", "unpaidoverdue", "unbooked"],
            [
                new("bookkeep", "PUT", "bookkeep", "Bookkeep an invoice (creates the voucher; cannot be undone)", true),
                new("cancel", "PUT", "cancel", "Cancel an unbooked invoice", true),
                new("credit", "PUT", "credit", "Create a credit invoice for a booked invoice", true),
                new("email", "GET", "email", "Send the invoice to the customer by email", true),
                new("einvoice", "GET", "einvoice", "Send the invoice as an e-invoice", true),
                new("external-print", "PUT", "externalprint", "Mark the invoice as sent (printed outside Fortnox)"),
                new("warehouse-ready", "PUT", "warehouseready", "Mark the invoice as ready for the warehouse")
            ],
            "Booked invoices cannot be updated or cancelled - credit them instead.",
            Pdf: true),

        new("invoice-payments", "invoicepayments", "InvoicePayments", "InvoicePayment", "Number", "payment",
            "Payments received against customer invoices", Ops.Read | Ops.Create | Ops.Update | Ops.Delete, null,
            [new("bookkeep", "PUT", "bookkeep", "Bookkeep a payment (cannot be undone)", true)]),

        new("supplier-invoices", "supplierinvoices", "SupplierInvoices", "SupplierInvoice", "GivenNumber", "supplierinvoice",
            "Supplier invoices (accounts payable)", Ops.Read | Ops.Write,
            ["cancelled", "fullypaid", "unpaid", "unpaidoverdue", "unbooked", "pendingpayment", "authorizepending"],
            [
                new("bookkeep", "PUT", "bookkeep", "Bookkeep a supplier invoice (cannot be undone)", true),
                new("cancel", "PUT", "cancel", "Cancel a supplier invoice", true),
                new("credit", "PUT", "credit", "Credit a supplier invoice", true),
                new("approve-payment", "PUT", "approvalpayment", "Approve a supplier invoice for payment", true),
                new("approve-bookkeep", "PUT", "approvalbookkeep", "Approve a supplier invoice for bookkeeping", true)
            ]),

        new("supplier-payments", "supplierinvoicepayments", "SupplierInvoicePayments", "SupplierInvoicePayment", "Number", "payment",
            "Payments made against supplier invoices", Ops.Read | Ops.Create | Ops.Update | Ops.Delete, null,
            [new("bookkeep", "PUT", "bookkeep", "Bookkeep a supplier payment (cannot be undone)", true)]),

        new("orders", "orders", "Orders", "Order", "DocumentNumber", "order",
            "Sales orders", Ops.Read | Ops.Write,
            ["cancelled", "expired", "invoicecreated", "invoicenotcreated"],
            [
                new("create-invoice", "PUT", "createinvoice", "Create an invoice from the order"),
                new("cancel", "PUT", "cancel", "Cancel an order", true),
                new("email", "GET", "email", "Send the order to the customer by email", true)
            ],
            Pdf: true),

        new("offers", "offers", "Offers", "Offer", "DocumentNumber", "offer",
            "Offers (quotes)", Ops.Read | Ops.Write,
            ["cancelled", "expired", "completed", "notcompleted"],
            [
                new("create-order", "PUT", "createorder", "Create an order from the offer"),
                new("create-invoice", "PUT", "createinvoice", "Create an invoice from the offer"),
                new("cancel", "PUT", "cancel", "Cancel an offer", true),
                new("email", "GET", "email", "Send the offer to the customer by email", true)
            ],
            Pdf: true),

        new("vouchers", "vouchers", "Vouchers", "Voucher", "VoucherSeries/VoucherNumber", "bookkeeping",
            "Vouchers (verifikationer)", Ops.Read | Ops.Create,
            Note: "Ids are SERIES/NUMBER, e.g. 'A/12'. Pass --query financialyear=<id> to read outside the current year. Vouchers cannot be changed or deleted once created - only reversed with a new voucher."),

        new("voucher-series", "voucherseries", "VoucherSeriesCollection", "VoucherSeries", "Code", "bookkeeping",
            "Voucher series", Ops.Read | Ops.Write),

        new("ledger-accounts", "accounts", "Accounts", "Account", "Number", "bookkeeping",
            "Chart of accounts (kontoplan)", Ops.Read | Ops.Write,
            Note: "Pass --query financialyear=<id> to read another year's chart."),

        new("financial-years", "financialyears", "FinancialYears", "FinancialYear", "Id", "bookkeeping",
            "Financial years", Ops.Read | Ops.Create,
            Note: "Pass --query date=YYYY-MM-DD to find the year a date falls in."),

        new("projects", "projects", "Projects", "Project", "ProjectNumber", "project",
            "Projects", Ops.All),

        new("cost-centers", "costcenters", "CostCenters", "CostCenter", "Code", "costcenter",
            "Cost centers (kostnadsställen)", Ops.All),

        new("terms-of-payments", "termsofpayments", "TermsOfPayments", "TermsOfPayment", "Code", "settings",
            "Terms of payment", Ops.All),

        new("currencies", "currencies", "Currencies", "Currency", "Code", "currency",
            "Currencies and exchange rates", Ops.All),

        new("price-lists", "pricelists", "PriceLists", "PriceList", "Code", "price",
            "Price lists", Ops.Read | Ops.Write),

        new("units", "units", "Units", "Unit", "Code", "article",
            "Units of measure", Ops.All)
    ];

    /// <summary>The scopes every built-in command needs, which is what <c>accounts add</c> asks for by default.</summary>
    public static string DefaultScopes =>
        string.Join(" ", All.Select(r => r.Scope).Append("companyinformation").Append("settings").Distinct().Order());
}
