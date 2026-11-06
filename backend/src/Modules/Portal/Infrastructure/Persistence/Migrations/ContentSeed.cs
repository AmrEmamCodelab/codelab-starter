using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Persistence.Migrations;

/// <summary>One seeded section.</summary>
/// <param name="Page">The page.</param>
/// <param name="SortOrder">The display order (steps of 10 leave room between).</param>
/// <param name="Text">The Arabic and English title and body.</param>
public sealed record SeedSection(PortalPage Page, int SortOrder, BilingualText Text);

/// <summary>A title and a body in Arabic and English (body paragraphs separated by <see cref="ContentSeed.ParagraphBreak"/>).</summary>
/// <param name="TitleAr">The Arabic title.</param>
/// <param name="TitleEn">The English title.</param>
/// <param name="BodyAr">The Arabic body.</param>
/// <param name="BodyEn">The English body.</param>
public sealed record BilingualText(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

/// <summary>
/// The content <see cref="InitialCreate"/> seeds: the starter wording the portal pages showed from their static
/// bundles before phase 5 (generic, product-neutral text; each project rewrites it in the admin editor). The service
/// guide's and legal pages' bullet items became paragraphs of their section. Never edit a shipped seed: change content
/// in the editor, or ship a new migration.
/// </summary>
public static class ContentSeed
{
    /// <summary>The separator between two paragraphs of a body: one blank line.</summary>
    public const string ParagraphBreak = "\n\n";

    private const int SortStep = 10;

    /// <summary>Every seeded section, page by page.</summary>
    public static IReadOnlyList<SeedSection> Sections { get; } =
    [
        .. Numbered(PortalPage.Faq, SeedFaq.Questions),
        .. Numbered(PortalPage.ServiceGuide, SeedServiceGuide.Sections),
        .. Numbered(PortalPage.Privacy, SeedLegal.Privacy),
        .. Numbered(PortalPage.Terms, SeedLegal.Terms),
    ];

    /// <summary>A body of <paramref name="paragraphs"/> separated by <see cref="ParagraphBreak"/>.</summary>
    public static string Paragraphs(params string[] paragraphs) => string.Join(ParagraphBreak, paragraphs);

    private static IEnumerable<SeedSection> Numbered(PortalPage page, IReadOnlyList<BilingualText> texts) =>
        texts.Select((text, index) => new SeedSection(page, SortOrder: (index + 1) * SortStep, text));
}

/// <summary>The seeded questions of the FAQ (one section per question).</summary>
internal static class SeedFaq
{
    public static IReadOnlyList<BilingualText> Questions { get; } =
    [
        new("ماذا يمكنني أن أفعل في هذه البوابة؟", "What can I do on this portal?",
            "يمكنك التعرف على الخدمة، وتقديم طلبك إلكترونيًا، ومتابعة سيره، والتواصل مع الفريق.",
            "You can read about the service, submit a request online, follow its progress and contact the team."),
        new("هل توجد رسوم لتقديم الطلب؟", "Is there a fee to submit a request?",
            "تقديم الطلب عبر البوابة مجاني، وإذا كان لنوع الطلب رسوم فسيوضحها لك النموذج قبل الإرسال.",
            "Submitting a request on the portal is free. If a request type has a fee, the form tells you before you submit."),
        new("هل يمكنني استخدام البوابة باللغة الإنجليزية؟", "Can I use the portal in English?",
            "نعم، يمكنك التبديل بين العربية والإنجليزية في أي وقت من زر اللغة أعلى الصفحة.",
            "Yes. Switch between Arabic and English at any time with the language button at the top of the page."),
        new("كيف أحصل على حساب؟", "How do I get an account?",
            "اختر تسجيل الدخول أعلى الصفحة، وإن لم يكن لديك حساب فاتبع الخطوات لإنشائه.",
            "Choose Sign in at the top of the page. If you have no account yet, follow the steps to create one."),
        new("نسيت كلمة المرور، فماذا أفعل؟", "I forgot my password. What should I do?",
            "اتبع رابط إعادة تعيين كلمة المرور في صفحة تسجيل الدخول، وإن تعذّر عليك الدخول بعد ذلك فتواصل معنا.",
            "On the sign-in page, follow the password reset link. If you still cannot sign in, contact us."),
        new("كيف أتابع طلبي؟", "How do I follow my request?",
            "سجّل الدخول وافتح طلباتك، وستجد لكل طلب حالته الحالية وسجل إجراءاته.",
            "Sign in and open your requests. Each request shows its current status and its history."),
        new("لماذا أُعيد إليّ طلبي؟", "Why was my request returned to me?",
            "يُعاد الطلب عند نقص معلومة أو مستند. اقرأ السبب، وحدّث الطلب، ثم أرسله من جديد.",
            "A request is returned when information or a document is missing. Read the reason, update the request and submit it again."),
        new("كم يستغرق صدور القرار؟", "How long does a decision take?",
            "يصدر القرار في معظم الطلبات خلال أيام عمل قليلة، وتعرض صفحة الحالة التاريخ المتوقع لطلبك.",
            "Most requests are decided within a few working days. The status page shows the expected date for your request."),
    ];
}

/// <summary>The seeded sections of the service guide.</summary>
internal static class SeedServiceGuide
{
    public static IReadOnlyList<BilingualText> Sections { get; } =
    [
        new("عن الخدمة", "About the service",
            ContentSeed.Paragraphs(
                "تتيح لك هذه البوابة تقديم طلبك إلكترونيًا ومتابعته حتى صدور القرار، دون الحاجة إلى مراجعة أي مكتب.",
                "يراجع الفريق المختص كل طلب، ونُعلمك في كل خطوة بما سيحدث بعدها."),
            ContentSeed.Paragraphs(
                "This portal lets you submit a request online and follow it until a decision is made, without visiting an office.",
                "Each request is reviewed by the responsible team, and you are told at every step what happens next.")),
        new("من يحق له التقديم", "Who can apply",
            ContentSeed.Paragraphs(
                "يمكنك تقديم طلب إذا انطبقت عليك إحدى الحالات الآتية:",
                "أن تكون مواطنًا أو مقيمًا لديك وثيقة هوية سارية.",
                "أن تمثّل جهة مسجّلة وتكون مفوّضًا بالتصرف نيابةً عنها.",
                "أن تتقدم نيابةً عن شخص آخر وبحوزتك تفويض ساري.",
                "أن يكون لديك حساب في هذه البوابة أو تستطيع إنشاء حساب."),
            ContentSeed.Paragraphs(
                "You can submit a request if one of the following applies to you:",
                "You are a citizen or resident with a valid identity document.",
                "You represent a registered organization and are authorised to act for it.",
                "You act for another person and hold a valid authorisation.",
                "You have an account on this portal, or can create one.")),
        new("المستندات المطلوبة", "Documents to prepare",
            ContentSeed.Paragraphs(
                "جهّز هذه المستندات قبل البدء، إذ سترفقها أثناء تعبئة الطلب.",
                "صورة واضحة من وثيقة الهوية.",
                "أي مستند داعم يطلبه نوع الطلب.",
                "خطاب تفويض إذا كنت تتقدم نيابةً عن شخص آخر.",
                "ملفات بصيغة PDF أو JPG أو PNG، لا يتجاوز حجم كل منها الحد الموضح في النموذج."),
            ContentSeed.Paragraphs(
                "Have these ready before you start; you attach them while filling in the request.",
                "A clear copy of your identity document.",
                "Any supporting document your request type asks for.",
                "An authorisation letter when you apply for someone else.",
                "Files in PDF, JPG or PNG format, each within the size shown on the form.")),
        new("طريقة التقديم", "How to apply",
            ContentSeed.Paragraphs(
                "يمر الطلب بخمس خطوات.",
                "الخطوة 1: سجّل الدخول. سجّل الدخول إلى البوابة، أو أنشئ حسابًا إذا لم يكن لديك حساب.",
                "الخطوة 2: عبّئ الطلب. اختر نوع الطلب وأدخل البيانات المطلوبة.",
                "الخطوة 3: أرفق المستندات. ارفع المستندات المذكورة أعلاه، ويمكنك استبدال أي ملف قبل الإرسال.",
                "الخطوة 4: راجع الطلب وأرسله. تحقق من بياناتك ثم أرسل الطلب، وستحصل على رقم مرجعي فورًا.",
                "الخطوة 5: تابع طلبك. تابع حالة طلبك في أي وقت، وأجب عن أي استفسار يرسله إليك الفريق."),
            ContentSeed.Paragraphs(
                "A request takes five steps.",
                "Step 1: Sign in. Sign in to the portal, or create an account if you do not have one.",
                "Step 2: Fill in the request. Choose the request type and enter the details it asks for.",
                "Step 3: Attach your documents. Upload the documents listed above. You can replace a file before you submit.",
                "Step 4: Review and submit. Check your details, then submit. You receive a reference number at once.",
                "Step 5: Follow your request. Track its status at any time and answer any question the team sends you.")),
        new("قبل إرسال الطلب", "Before you submit",
            "تأكد من اكتمال بياناتك ومستنداتك وصحتها؛ فقد يُعاد إليك الطلب غير المكتمل لاستكماله، وهذا يستغرق وقتًا أطول.",
            "Check that your details and documents are complete and correct. An incomplete request may be returned to you for changes, which takes longer."),
    ];
}

/// <summary>The seeded sections of the privacy policy and the terms of use.</summary>
internal static class SeedLegal
{
    public static IReadOnlyList<BilingualText> Privacy { get; } =
    [
        new("البيانات التي نجمعها", "What we collect",
            ContentSeed.Paragraphs(
                "لا نجمع إلا البيانات التي نحتاج إليها لتقديم الخدمة:",
                "البيانات التي تُدخلها في حسابك وطلباتك، مثل اسمك وبيانات التواصل معك.",
                "المستندات التي ترفقها بطلبك.",
                "سجلات تقنية لزياراتك، مثل وقت تسجيل الدخول، نحتفظ بها لأغراض الأمان."),
            ContentSeed.Paragraphs(
                "We collect only the information we need to provide the service:",
                "The details you enter in your account and your requests, such as your name and contact details.",
                "The documents you attach to a request.",
                "Technical records of your visits, such as the time of sign-in, kept for security.")),
        new("كيف نستخدمها", "How we use it",
            "نستخدم بياناتك لمعالجة طلباتك والتواصل معك بشأنها والحفاظ على أمان البوابة، ولا نستخدمها لأغراض إعلانية.",
            "We use your information to handle your requests, to contact you about them and to keep the portal secure. We do not use it for advertising."),
        new("مع من نشاركها", "Who we share it with",
            "لا يطّلع على بياناتك إلا الموظفون المختصون بمعالجة طلبك، ولا نشاركها مع أي جهة أخرى إلا إذا اقتضى النظام ذلك.",
            "Your information is seen only by the staff who handle your request. We share it with another party only when the law requires it."),
        new("مدة الاحتفاظ بها", "How long we keep it",
            "نحتفظ ببياناتك طوال المدة التي تقتضيها قواعد حفظ السجلات المعمول بها، ثم نحذفها بطريقة آمنة.",
            "We keep your information for as long as the applicable record-keeping rules require, then delete it securely."),
        new("حقوقك", "Your rights",
            ContentSeed.Paragraphs(
                "يمكنك التواصل معنا في أي وقت لتطلب ما يلي:",
                "معرفة البيانات الشخصية التي نحتفظ بها عنك.",
                "تصحيح أي بيانات غير صحيحة.",
                "معرفة كيفية استخدام بياناتك."),
            ContentSeed.Paragraphs(
                "You can contact us at any time to:",
                "Ask what personal information we hold about you.",
                "Ask us to correct information that is wrong.",
                "Ask how your information has been used.")),
        new("ملفات تعريف الارتباط", "Cookies",
            "لا تستخدم البوابة إلا ملفات تعريف الارتباط اللازمة لإبقائك مسجّل الدخول وتذكّر لغتك، ولا تستخدم أي ملفات للتتبع.",
            "The portal uses only the cookies it needs to keep you signed in and to remember your language. It uses no tracking cookies."),
    ];

    public static IReadOnlyList<BilingualText> Terms { get; } =
    [
        new("قبول الشروط", "Accepting these terms",
            "باستخدامك هذه البوابة فإنك توافق على هذه الشروط، وإن لم توافق عليها فيُرجى عدم استخدام البوابة.",
            "By using this portal you accept these terms. If you do not accept them, please do not use the portal."),
        new("حسابك", "Your account",
            "أنت مسؤول عن الحفاظ على سرية بيانات دخولك وعن كل ما يتم من خلال حسابك، فأبلغنا فورًا إذا ظننت أن غيرك استخدمه.",
            "You are responsible for keeping your sign-in details secret and for everything done with your account. Tell us at once if you think someone else has used it."),
        new("الاستخدام المقبول", "Acceptable use",
            ContentSeed.Paragraphs(
                "عند استخدامك البوابة فإنك تتعهد بألّا:",
                "تُدخل بيانات غير صحيحة أو بيانات شخص آخر دون تفويض منه.",
                "تحاول الوصول إلى أجزاء من البوابة أو بيانات غير مسموح لك بالاطلاع عليها.",
                "تعطّل عمل البوابة، كإرسال طلبات آلية أو مفرطة."),
            ContentSeed.Paragraphs(
                "When you use the portal, you agree not to:",
                "Enter false information or another person's details without their authorisation.",
                "Try to reach parts of the portal or data you are not allowed to see.",
                "Disrupt the portal, for example by sending automated or excessive requests.")),
        new("المعلومات المنشورة في البوابة", "Information on the portal",
            "نحرص على أن تكون المعلومات المنشورة في البوابة دقيقة ومحدّثة، والقرار المعتمد في طلبك هو المسجل في صفحة حالته.",
            "We work to keep the information on the portal accurate and up to date. The decision on your request is the one recorded on its status page."),
        new("حدود المسؤولية", "Limits of responsibility",
            "قد تتوقف البوابة أحيانًا لأعمال الصيانة، ولا نتحمل مسؤولية أي ضرر ينتج عن انقطاع خارج عن إرادتنا.",
            "The portal may be unavailable at times for maintenance. We are not responsible for losses caused by an interruption beyond our control."),
        new("تعديل الشروط", "Changes to these terms",
            "قد نحدّث هذه الشروط، والنسخة المنشورة في هذه الصفحة هي النسخة المعمول بها.",
            "We may update these terms. The version published on this page is the one that applies."),
    ];
}
