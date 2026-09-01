USE [master];
GO
IF DB_ID(N'QatFarmDb') IS NULL
BEGIN
    CREATE DATABASE [QatFarmDb];
END
GO
ALTER DATABASE [QatFarmDb] SET RECOVERY FULL;
GO
-- الجداول وحساب المدير والبيانات الأساسية ينشئها التطبيق آليًا عند أول تشغيل.
-- بعد التشغيل الأول خذ نسخة احتياطية كاملة وجدول نسخة يومية من SQL Server Agent.
