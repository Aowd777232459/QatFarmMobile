using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Models;

namespace QatFarm.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        await DatabaseSchemaUpdater.ApplyAsync(db);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Administrator", "Accountant", "Employee" })
        {
            if (await roleManager.RoleExistsAsync(role)) continue;
            var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(string.Join(" | ", roleResult.Errors.Select(x => x.Description)));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = configuration["SeedAdmin:Email"] ?? "abdulmalik.awad@qat.local";
        var password = Environment.GetEnvironmentVariable("QAT_ADMIN_PASSWORD")
                       ?? configuration["SeedAdmin:TemporaryPassword"]
                       ?? "Qat@2026#ChangeMe";
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = configuration["SeedAdmin:FullName"] ?? "عبد الملك عواد",
                MustChangePassword = true,
                IsActive = true
            };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(" | ", result.Errors.Select(x => x.Description)));
        }

        if (!await userManager.IsInRoleAsync(admin, "Administrator"))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, "Administrator");
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(string.Join(" | ", roleResult.Errors.Select(x => x.Description)));
        }

        var cultivationNames = new[]
        {
            "السقي", "حساب العمال", "سم حديدي", "سماد", "مبيدات", "تراب", "نقل", "أدوات زراعية", "صيانة", "مصروف آخر"
        };
        var existingCultivationNames = await db.CultivationExpenseTypes
            .IgnoreQueryFilters().Select(x => x.Name).ToListAsync();
        db.CultivationExpenseTypes.AddRange(cultivationNames
            .Except(existingCultivationNames)
            .Select(x => new CultivationExpenseType { Name = x }));

        var qatNames = new[]
        {
            "أميال رقم واحد", "أميال مخضر", "بزغة رقم واحد", "بزغة مخضر"
        };
        var existingQatNames = await db.QatTypes
            .IgnoreQueryFilters().Select(x => x.Name).ToListAsync();
        db.QatTypes.AddRange(qatNames
            .Except(existingQatNames)
            .Select(x => new QatType { Name = x }));

        var dailyExpenseNames = new[]
        {
            "صرفة عمال", "حساب عمال", "مبيدات", "تراب", "السقي", "نقل", "تعبئة", "تحميل", "عمولة", "مصروف آخر"
        };
        var existingDailyExpenseNames = await db.DailyExpenseTypes
            .IgnoreQueryFilters().Select(x => x.Name).ToListAsync();
        db.DailyExpenseTypes.AddRange(dailyExpenseNames
            .Except(existingDailyExpenseNames)
            .Select(x => new DailyExpenseType { Name = x }));

        if (!await db.SystemSettings.AnyAsync(x => x.Key == "DefaultZakatPercent"))
            db.SystemSettings.Add(new SystemSetting { Key = "DefaultZakatPercent", Value = "5", Description = "نسبة الزكاة الافتراضية" });
        if (!await db.SystemSettings.AnyAsync(x => x.Key == "Currency"))
            db.SystemSettings.Add(new SystemSetting { Key = "Currency", Value = "ريال يمني", Description = "عملة النظام" });
        if (!await db.SystemSettings.AnyAsync(x => x.Key == "InvoicePrefix"))
            db.SystemSettings.Add(new SystemSetting { Key = "InvoicePrefix", Value = "INV", Description = "بادئة رقم الفاتورة" });

        var defaultAccounts = new[]
        {
            new ChartOfAccount { Code = "1101", Name = "الصندوق", Category = AccountCategory.Asset, IsSystem = true, Notes = "النقدية المتاحة بالصندوق" },
            new ChartOfAccount { Code = "1102", Name = "البنك والتحويلات", Category = AccountCategory.Asset, IsSystem = true, Notes = "الأرصدة البنكية والتحويلات" },
            new ChartOfAccount { Code = "1201", Name = "حسابات العملاء المدينة", Category = AccountCategory.Asset, IsSystem = true, Notes = "المبالغ المستحقة على العملاء" },
            new ChartOfAccount { Code = "2101", Name = "حسابات الدائنين", Category = AccountCategory.Liability, IsSystem = true, Notes = "المبالغ المستحقة للدائنين" },
            new ChartOfAccount { Code = "2201", Name = "الزكاة المستحقة", Category = AccountCategory.Liability, IsSystem = true, Notes = "الزكاة المثبتة ولم يتم سدادها بعد" },
            new ChartOfAccount { Code = "3101", Name = "حقوق الملكية والأرصدة الافتتاحية", Category = AccountCategory.Equity, IsSystem = true },
            new ChartOfAccount { Code = "4101", Name = "إيرادات بيع القات", Category = AccountCategory.Revenue, IsSystem = true },
            new ChartOfAccount { Code = "5101", Name = "مصروفات وخسائر التربية", Category = AccountCategory.Expense, IsSystem = true },
            new ChartOfAccount { Code = "5201", Name = "مصروفات البيع والتشغيل", Category = AccountCategory.Expense, IsSystem = true },
            new ChartOfAccount { Code = "5301", Name = "مصروف الزكاة", Category = AccountCategory.Expense, IsSystem = true },
            new ChartOfAccount { Code = "5901", Name = "مصروفات أخرى", Category = AccountCategory.Expense, IsSystem = true }
        };
        var existingAccountCodes = await db.ChartOfAccounts.IgnoreQueryFilters().Select(x => x.Code).ToListAsync();
        db.ChartOfAccounts.AddRange(defaultAccounts.Where(x => !existingAccountCodes.Contains(x.Code)));

        await db.SaveChangesAsync();
    }
}
