# الهيكل المعماري

## اختيار التقنية

تم استخدام Blazor Interactive Server كتطبيق ويب متجاوب بدل اتصال تطبيق الجوال مباشرةً بقاعدة البيانات. هذا يمنع كشف منفذ SQL Server وبيانات الدخول على الهواتف، ويوحّد الواجهة بين Windows والجوال.

## الطبقات داخل المشروع

- `Models`: الكيانات والنماذج الحسابية.
- `Data`: DbContext وتهيئة قاعدة البيانات والبيانات الأساسية.
- `Services`: منطق المزارع والخسائر والفواتير والحسابات والتقارير.
- `Components`: واجهات Blazor العربية.
- `Pages/Account`: الدخول وتغيير كلمة المرور والخروج.
- `database`: سكربتات SQL Server.

## العلاقات الأساسية

```text
Farm 1 --- * CultivationExpense
Farm 1 --- * SalesInvoice
SalesInvoice 1 --- * SalesInvoiceItem
SalesInvoice 1 --- * InvoiceExpense
QatType 1 --- * SalesInvoiceItem
DailyExpenseType 1 --- * InvoiceExpense
CultivationExpenseType 1 --- * CultivationExpense
```

## الدقة المالية

جميع المبالغ مخزنة كـ `decimal(18,2)`، وليست `float` أو `double`. التقريب للزكاة يستخدم `MidpointRounding.AwayFromZero`، والفاتورة الملغاة لا تدخل في مؤشرات المبيعات.

## الحماية

- Identity وأدوار Administrator / Accountant / Employee.
- الحذف Soft Delete للبيانات الأساسية والخسائر.
- إلغاء الفواتير بدل حذفها.
- AuditLog للعمليات المالية.
- RowVersion لمراقبة تعارض التعديلات.
- Unique Index لأسماء القوائم ورقم الفاتورة.

## توسعة العملاء والزكاة

أصبح مسار العمليات المالية:

`Customer -> SalesInvoice -> SalesInvoiceItem / InvoiceExpense -> CustomerPayment`

وتحمل الفاتورة حالة زكاة مستقلة لا تتأثر بحالة سداد العميل:

`Pending -> Paid`

الحذف المالي هو Soft Delete، بينما الإلغاء يحتفظ بالفاتورة ظاهرة بحالة Cancelled. لا يسمح النظام بحذف أو إلغاء فاتورة مرتبطة بسند قبض أو بزكاة مؤكدة الدفع.
