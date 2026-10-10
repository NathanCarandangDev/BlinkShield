using UnityEngine;
using SQLite4Unity3d;

/// <summary>
/// Initializes all required tables in the SQLite database.
/// Safe to call multiple times—tables will only be created if they do not exist.
/// Will not overwrite existing data.
/// </summary>
public static class DBInitializer
{
    public static void CreateTables(SQLiteConnection db)
    {
        if (db == null)
        {
            Debug.LogError("[DBInitializer] SQLiteConnection is null.");
            return;
        }

        // Core user-related tables
        db.CreateTable<User>();
        db.CreateTable<UserSettings>();
        db.CreateTable<UserWardrobe>();
        db.CreateTable<UserAchievement>();
        

        db.CreateTable<WardrobeItem>();

        db.CreateTable<EyeHealthLog>();
        db.CreateTable<EyeIssue>();
        db.CreateTable<EyeTrivia>();
        db.CreateTable<EyeTip>();

        db.CreateTable<Achievement>();
        db.CreateTable<ScreenTimeHistory>();
        
        db.CreateTable<Feedback>();
        db.CreateTable<AdminHistory>();

        Debug.Log("[DBInitializer] All tables created or verified (existing data preserved).");
    }

    // Enhanced method to verify all tables exist
    public static void VerifyAllTables(SQLiteConnection db)
    {
        if (db == null)
        {
            Debug.LogError("[DBInitializer] SQLiteConnection is null.");
            return;
        }

        var requiredTables = new[]
        {
            "users", "user_settings", "user_wardrobe", "user_achievements",
             "wardrobe_items",
            "eyehealth_log", "eye_issues", "eye_trivias", "eye_tips",
            "achievements", "screentime_history", 
            "feedbacks", "admin_history"
        };

        foreach (var tableName in requiredTables)
        {
            try
            {
                var result = db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", 
                    tableName);
                
                if (result == 0)
                {
                    Debug.LogError($"[DBInitializer] MISSING TABLE: {tableName}");
                }
                else
                {
                    Debug.Log($"[DBInitializer] Table verified: {tableName}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DBInitializer] Error checking table {tableName}: {ex.Message}");
            }
        }
    }

    // Emergency repair method
    public static void RepairMissingTables(SQLiteConnection db)
    {
        if (db == null) return;

        Debug.LogWarning("[DBInitializer] Repairing missing tables...");

        // List of all table types that should exist
        var tableTypes = new System.Type[]
        {
            typeof(User),
            typeof(UserSettings),
            typeof(UserWardrobe),
            typeof(UserAchievement),
            typeof(WardrobeItem),
            typeof(EyeHealthLog),
            typeof(EyeIssue),
            typeof(EyeTrivia),
            typeof(EyeTip),
            typeof(Achievement),
            typeof(ScreenTimeHistory),
            typeof(Feedback),
            typeof(AdminHistory)
        };

        foreach (var tableType in tableTypes)
        {
            try
            {
                var tableName = GetTableName(tableType);
                if (!TableExists(db, tableName))
                {
                    Debug.LogWarning($"[DBInitializer] Creating missing table: {tableName}");
                    db.CreateTable(tableType);
                    Debug.Log($"[DBInitializer] Created table: {tableName}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DBInitializer] Failed to create table {tableType.Name}: {ex.Message}");
            }
        }

        Debug.Log("[DBInitializer] Table repair completed");
    }

    private static bool TableExists(SQLiteConnection db, string tableName)
    {
        try
        {
            var result = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", 
                tableName);
            return result > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTableName(System.Type tableType)
    {
        var tableAttribute = (TableAttribute)System.Attribute.GetCustomAttribute(tableType, typeof(TableAttribute));
        return tableAttribute?.Name ?? tableType.Name;
    }
}