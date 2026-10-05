using System.Globalization;
using BookStore.Application.Common.Abstractions;

namespace BookStore.Application.Features.Content;

/// <summary>
/// Writes the "how it works" page. The text describes the workflow the platform
/// actually enforces, and every number in it is read from the live settings, so the
/// page cannot promise a fee or a delivery charge the checkout does not apply.
/// </summary>
public sealed class HowItWorksService
{
    private readonly IPlatformSettings _platform;

    public HowItWorksService(IPlatformSettings platform) => _platform = platform;

    public HowItWorksContent Get()
    {
        var figures = new PlatformFigures(
            _platform.Currency,
            _platform.FeePercent,
            _platform.ShippingCost,
            (int)_platform.CheckoutReservationWindow.TotalMinutes,
            (int)_platform.WalletSettlementPeriod.TotalDays);

        var fee = Number(figures.FeePercent);
        var shippingAr = Money(figures.ShippingCost, figures.Currency, arabic: true);
        var shippingEn = Money(figures.ShippingCost, figures.Currency, arabic: false);
        var hold = new Phrase(
            ArabicCount(figures.ReservationMinutes, "دقيقة", "دقيقتين", "دقائق", "دقيقة"),
            $"{figures.ReservationMinutes} minutes");
        var payout = figures.SettlementDays <= 0
            ? new Phrase("فور اكتمال الطلب", "as soon as the order completes")
            : new Phrase(
                $"بعد {ArabicCount(figures.SettlementDays, "يوم", "يومين", "أيام", "يومًا")} من اكتمال الطلب",
                figures.SettlementDays == 1
                    ? "1 day after the order completes"
                    : $"{figures.SettlementDays} days after the order completes");

        return new HowItWorksContent(
            figures,
            [Selling(fee, payout), Buying(shippingAr, shippingEn, hold)],
            Rules(fee, shippingAr, shippingEn),
            Questions(figures.FeePercent, hold, payout));
    }

    private static Journey Selling(string fee, Phrase payout) => new(
        "sell",
        "بيع كتاب",
        "Selling a book",
        "كل إعلان على المنصة نسخة حقيقية واحدة. نحن لا نعرض كتابًا قبل أن نراه بأنفسنا: نراجع إعلانك، ونستلم النسخة في مخزننا، ونضعها على رف، وعندها فقط تظهر للمشترين.",
        "Every listing on the platform is one real copy. We never show a book we have not seen: we review your listing, receive the copy in our warehouse and put it on a shelf, and only then does it appear to buyers.",
        [
            new(
                "اكتب الإعلان",
                "Write the listing",
                "سجّل الدخول وأضف كتابك: العنوان والمؤلف والتصنيف والسعر وحالة النسخة. يمكنك تصوير الغلاف لنقترح عليك البيانات، وتراجعها أنت قبل الحفظ. الإعلان يبقى مسودة لا يراها أحد غيرك.",
                "Sign in and add your book: title, author, category, price and condition. You can photograph the cover and we will suggest the details for you to check before saving. The listing stays a draft that nobody else can see."),
            new(
                "صوّر النسخة",
                "Photograph the copy",
                "أضف صورًا واضحة للنسخة نفسها. صورة الغلاف مطلوبة قبل الإرسال للمراجعة، وصور الصفحات والعيوب تساعد المشتري على الثقة.",
                "Add clear photographs of the copy itself. A cover photograph is required before review, and photographs of the pages and any wear help buyers trust it."),
            new(
                "أرسل للمراجعة",
                "Send it for review",
                "عندما يكتمل الإعلان أرسله للمراجعة. من هذه اللحظة لا يمكن تعديله، لأننا نراجع وصف نسخة بعينها.",
                "When the listing is complete, send it for review. From that moment it can no longer be edited, because we are reviewing the description of one particular copy."),
            new(
                "ننتظر قرار المراجعة",
                "We review it",
                "يفحص فريقنا البيانات والصور والسعر. إذا وافقنا نطلب منك إرسال النسخة. وإذا رفضنا يصلك السبب في الإشعارات، فتعدّل الإعلان نفسه وترسله مرة أخرى دون أن تبدأ من جديد.",
                "Our team checks the details, the photographs and the price. If we approve, we ask you to send the copy in. If we decline, the reason reaches you in your notifications, and you edit the same listing and send it again without starting over."),
            new(
                "أرسل النسخة إلى المخزن",
                "Send the copy to the warehouse",
                "الموافقة لا تعني أن الكتاب معروض للبيع بعد. نستلم النسخة ونتأكد أنها تطابق الإعلان، ثم نضعها على رف في المخزن.",
                "Approval does not put the book on sale yet. We receive the copy, check that it matches the listing, then give it a shelf in the warehouse."),
            new(
                "يُعرض للبيع",
                "It goes on sale",
                "بمجرد وضع النسخة على الرف تظهر في المتجر ونتائج البحث. نحن نتولى الدفع والتغليف والشحن، ولا يرى المشتري بياناتك ولا ترى أنت بياناته.",
                "As soon as the copy is shelved it appears in the store and in search. We handle payment, packing and delivery, and the buyer never sees your details, nor you theirs."),
            new(
                "تستلم أرباحك",
                "You get paid",
                $"بعد اكتمال الطلب تُضاف أرباحك إلى محفظتك: سعر البيع مخصومًا منه عمولة المنصة {fee}%. وتصبح متاحة للسحب {payout.Ar}.",
                $"Once the order completes, your earnings go to your wallet: the sale price minus the platform's {fee}% commission. They can be withdrawn {payout.En}."),
        ]);

    private static Journey Buying(string shippingAr, string shippingEn, Phrase hold) => new(
        "buy",
        "شراء كتاب",
        "Buying a book",
        "كل كتاب تراه في المتجر فحصناه بأنفسنا وهو موجود على رف في مخزننا، لذلك ما تراه في الصور والوصف هو ما يصلك.",
        "Every book in the store has been inspected by us and is sitting on a shelf in our warehouse, so what you see in the photographs and description is what arrives.",
        [
            new(
                "ابحث واختر",
                "Search and choose",
                "تصفّح التصنيفات أو ابحث بالعنوان أو المؤلف، وضيّق النتائج بالسعر والحالة واللغة. صفحة كل كتاب تعرض صوره الحقيقية وحالة الغلاف والصفحات وأي ملاحظات.",
                "Browse the categories or search by title or author, and narrow the results by price, condition and language. Each book's page shows its real photographs, the state of its cover and pages, and any notes."),
            new(
                "أضف إلى السلة أو المفضلة",
                "Add to your basket or favourites",
                "السلة والمفضلة مرتبطتان بحسابك. وجود الكتاب في سلتك لا يحجزه لك: النسخة واحدة ويمكن لغيرك شراؤها حتى تُتم الطلب، وإذا بيعت أو تغيّر سعرها ننبّهك في السلة.",
                "Your basket and favourites belong to your account. Having a book in your basket does not hold it for you: there is one copy and someone else can buy it until you check out, and if it sells or its price changes, your basket tells you."),
            new(
                "أتمم الطلب",
                "Check out",
                $"اختر عنوان التوصيل وأكّد الطلب. عندها فقط تُحجز النسخ لك لمدة {hold.Ar} حتى تُتم الدفع. يُضاف إلى الطلب رسم شحن ثابت قدره {shippingAr} مهما كان عدد الكتب.",
                $"Choose a delivery address and confirm the order. Only then are the copies held for you, for {hold.En} while you pay. A flat delivery charge of {shippingEn} is added to the order however many books it has."),
            new(
                "نجهّز ونشحن",
                "We pack and ship",
                "نسحب الكتب من رفوفها ونغلّفها ونشحنها من مخزننا إلى عنوانك، ويمكنك متابعة حالة الطلب من صفحة طلباتي.",
                "We take the books off their shelves, pack them and ship them from our warehouse to your address, and you can follow the order from your orders page."),
            new(
                "أكّد الاستلام",
                "Confirm receipt",
                "عندما يصلك الطلب أكّد استلامه من صفحة الطلب، فيكتمل الطلب ويحصل البائع على أرباحه.",
                "When the parcel arrives, confirm it from the order page. The order completes and the seller is paid."),
        ]);

    private static IReadOnlyList<LocalizedEntry> Rules(
        string fee,
        string shippingAr,
        string shippingEn) =>
    [
        new(
            "نسخة واحدة لكل إعلان",
            "One copy per listing",
            "كل إعلان كتاب مادي واحد بحالته الخاصة وصوره الخاصة، لذلك لا توجد كميات في السلة.",
            "Each listing is one physical book with its own condition and photographs, which is why a basket has no quantities."),
        new(
            "عمولة المنصة",
            "Platform commission",
            $"نأخذ {fee}% من سعر البيع عند بيع الكتاب فقط. إضافة الإعلان ومراجعته مجانية.",
            $"We take {fee}% of the sale price, and only when the book sells. Listing and review are free."),
        new(
            "رسم شحن ثابت",
            "Flat delivery charge",
            $"يدفع المشتري {shippingAr} للطلب كله، سواء كان كتابًا واحدًا أو أكثر.",
            $"The buyer pays {shippingEn} for the whole order, whether it holds one book or several."),
        new(
            "خصوصية الطرفين",
            "Privacy on both sides",
            "المشتري لا يرى اسم البائع، والبائع لا يرى اسم المشتري ولا عنوانه. المنصة هي الوسيط في كل شيء.",
            "Buyers never see who sold a book, and sellers never see who bought it or where it went. The platform sits between the two."),
        new(
            "حالة النسخة",
            "Condition grades",
            "نستخدم ست درجات: جديد، شبه جديد، جيد جدًا، جيد، مقبول، ضعيف. ونذكر بوضوح وجود كتابة أو تظليل أو أي عيب.",
            "We use six grades: new, like new, very good, good, acceptable and poor, and we say plainly when there is writing, highlighting or any damage."),
    ];

    private static IReadOnlyList<LocalizedEntry> Questions(decimal feePercent, Phrase hold, Phrase payout)
    {
        var fee = Number(feePercent);
        var example = Number(100m - 100m * feePercent / 100m);

        return
        [
            new(
                "لماذا لا يظهر كتابي بعد الموافقة عليه؟",
                "Why isn't my book showing after it was approved?",
                "الموافقة تعني أننا ننتظر النسخة. يظهر الكتاب في المتجر بعد أن نستلمه ونضعه على رف في المخزن.",
                "Approval means we are expecting the copy. The book appears in the store once we have received it and put it on a shelf."),
            new(
                "هل يمكنني تعديل الإعلان بعد إرساله؟",
                "Can I edit a listing after sending it?",
                "لا، طالما هو قيد المراجعة. إذا رُفض يعود إليك مع السبب فتعدّله وترسله من جديد. ويمكنك حذف المسودة قبل إرسالها.",
                "Not while it is in review. If it is declined it comes back to you with the reason, so you can edit and resend it. You can delete a draft before sending it."),
            new(
                "كم أربح من بيع كتاب؟",
                "How much do I earn from a sale?",
                $"سعر البيع ناقص عمولة المنصة {fee}%. مثلًا: كتاب بسعر 100 يعطيك {example}. وتصبح الأرباح قابلة للسحب {payout.Ar}.",
                $"The sale price minus the platform's {fee}% commission. For example, a book priced at 100 earns you {example}. Earnings can be withdrawn {payout.En}."),
            new(
                "أضفت كتابًا إلى السلة ثم اختفى، لماذا؟",
                "A book in my basket disappeared. Why?",
                "السلة لا تحجز الكتب. كل إعلان نسخة واحدة، فإذا أتمّ مشترٍ آخر طلبه قبلك تُباع له وننبّهك في سلتك.",
                "A basket does not hold books. Each listing is a single copy, so if another buyer checks out first it is sold to them and your basket tells you."),
            new(
                "ماذا يحدث إذا لم أُكمل الدفع؟",
                "What happens if I don't finish paying?",
                $"تبقى الكتب محجوزة لك {hold.Ar}. بعدها يُلغى الطلب تلقائيًا وتعود الكتب للبيع. ويمكنك إلغاء أي طلب لم يُدفع بنفسك في أي وقت.",
                $"The books stay held for you for {hold.En}. After that the order is cancelled automatically and the books go back on sale. You can also cancel any unpaid order yourself at any time."),
            new(
                "هل يمكنني شراء كتاب عرضته أنا؟",
                "Can I buy a book I listed myself?",
                "لا، لا يمكن إضافة إعلاناتك إلى سلتك.",
                "No. Your own listings cannot be added to your basket."),
        ];
    }

    /// <summary>
    /// A count with the Arabic noun in the form the number takes: the singular for
    /// one, the dual for two, the plural for three to ten, and the accusative
    /// singular from eleven up.
    /// </summary>
    private static string ArabicCount(int count, string one, string two, string few, string many) =>
        count switch
        {
            1 => one,
            2 => two,
            >= 3 and <= 10 => $"{count} {few}",
            _ => $"{count} {many}",
        };

    private static string Number(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Money(decimal amount, string currency, bool arabic) =>
        arabic && currency == "EGP"
            ? $"{Number(amount)} جنيه"
            : $"{Number(amount)} {currency}";
}

/// <summary>The same phrase in both languages, built once from a setting.</summary>
internal sealed record Phrase(string Ar, string En);
