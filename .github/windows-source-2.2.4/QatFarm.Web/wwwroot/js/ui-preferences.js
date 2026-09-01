(() => {
    const translations = {
        "الرئيسية":"Home","المحاسبة":"Accounting","فاتورة":"Invoice","العملاء":"Customers","المزيد":"More",
        "خروج":"Logout","الإعدادات والنسخ الاحتياطي":"Settings & backup","إعدادات النظام والصيانة":"System settings & maintenance",
        "إعدادات النظام":"System settings","الفواتير":"Invoices","خسائر التربية":"Cultivation costs","المزارع":"Farms",
        "الدائنون":"Creditors","الزكاة":"Zakat","التقارير":"Reports","القوائم المحاسبية":"Catalogs",
        "المستخدمون":"Users","سجل التدقيق":"Audit log","إنشاء فاتورة بيع":"Create sales invoice",
        "تعديل فاتورة البيع":"Edit sales invoice","بيانات الفاتورة":"Invoice details","أصناف الفاتورة":"Invoice items",
        "مصروفات الفاتورة":"Invoice expenses","الدفع":"Payment","الملخص المالي":"Financial summary",
        "المزرعة":"Farm","العميل":"Customer","تاريخ الفاتورة":"Invoice date","تاريخ الاستحقاق":"Due date",
        "اسم المشتري":"Buyer name","هاتف المشتري":"Buyer phone","طريقة الدفع":"Payment method",
        "المبلغ المدفوع":"Amount paid","نقدي":"Cash","تحويل":"Transfer","آجل":"Credit","مختلط":"Mixed",
        "نوع القات":"Qat type","الكمية":"Quantity","سعر الوحدة":"Unit price","الإجمالي":"Total",
        "نوع المصروف":"Expense type","المبلغ":"Amount","البيان":"Description","ملاحظات الفاتورة":"Invoice notes",
        "إضافة صنف":"Add item","إضافة مصروف":"Add expense","حفظ واعتماد الفاتورة":"Save invoice",
        "إلغاء":"Cancel","حفظ":"Save","جديد":"New","فعال":"Active","موقوف":"Inactive","تعديل":"Edit",
        "لوحة التحكم المالية":"Financial dashboard","مؤشرات الأعمال":"Business indicators",
        "إدارة المزارع":"Farm management","العملاء والديون":"Customers & debts","الفواتير والمبيعات":"Invoices & sales",
        "التقارير والتحليلات":"Reports & analysis","المحاسبة والقيود":"Accounting & journals",
        "المستخدمون والصلاحيات":"Users & permissions","الإعدادات والصيانة":"Settings & maintenance",
        "بيع نقدي / بدون حساب عميل":"Cash sale / unregistered buyer","بيع مباشر بدون حساب عميل":"Direct cash sale",
        "اختر المزرعة":"Select farm","اختر نوع القات":"Select qat type","اختر النوع":"Select type",
        "نسخة احتياطية محلية":"Local backup","إنشاء ومشاركة نسخة":"Create & share backup",
        "استعادة نسخة":"Restore backup","معلومات التطبيق":"Application information","الإصدار":"Version",
        "سياسة الخصوصية":"Privacy policy","مزامنة Wi-Fi":"Wi-Fi sync","مزامنة الآن":"Sync now",
        "المظهر وحجم الخط واللغة":"Appearance, font & language","الوضع الفاتح":"Light mode",
        "الوضع الداكن":"Dark mode","الهوية الخضراء":"Green theme","العربية":"Arabic","الإنجليزية":"English",
        "صغير":"Small","متوسط":"Medium","كبير":"Large","تم الحفظ":"Saved"
    };

    let observer;
    const translateNode = node => {
        if (node.nodeType !== Node.TEXT_NODE) return;
        const value = node.nodeValue || "";
        const trimmed = value.trim();
        if (!trimmed) return;
        if (node.__awadOriginal === undefined) node.__awadOriginal = value;
        const language = document.documentElement.dataset.language || "ar";
        if (language === "en") {
            const translated = translations[trimmed];
            if (translated) node.nodeValue = value.replace(trimmed, translated);
        } else if (node.__awadOriginal !== undefined) {
            node.nodeValue = node.__awadOriginal;
        }
    };
    const translateTree = root => {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode())) translateNode(node);
    };
    const apply = prefs => {
        const theme = prefs.theme || "green";
        const scale = Number(prefs.fontScale || 1);
        const language = prefs.language === "en" ? "en" : "ar";
        document.documentElement.dataset.theme = theme;
        document.documentElement.dataset.language = language;
        document.documentElement.lang = language;
        document.documentElement.dir = language === "en" ? "ltr" : "rtl";
        document.body.style.zoom = String(Math.min(1.25, Math.max(.9, scale)));
        translateTree(document.body);
        if (!observer) {
            observer = new MutationObserver(changes => {
                for (const change of changes) {
                    for (const added of change.addedNodes) {
                        if (added.nodeType === Node.TEXT_NODE) translateNode(added);
                        else if (added.nodeType === Node.ELEMENT_NODE) translateTree(added);
                    }
                }
            });
            observer.observe(document.body, { childList: true, subtree: true });
        }
    };
    const get = () => {
        try { return JSON.parse(localStorage.getItem("awad.ui.preferences") || "{}"); } catch { return {}; }
    };
    window.awadUiPreferences = {
        get,
        save: prefs => { localStorage.setItem("awad.ui.preferences", JSON.stringify(prefs)); apply(prefs); },
        apply
    };
    const start = () => apply(get());
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();
