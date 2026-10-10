using SQLite4Unity3d;
using System;

// -------------------------------
// Interface for user-scoped tables
// -------------------------------
public interface IUserData
{
    int user_id { get; set; }
}

public interface IHasDate
{
    string date { get; set; } // SQLite-friendly string date
    DateTime DateTimeValue { get; set; } // optional helper for code
}
// ===================================================
// Core User Tables
// ===================================================
[Serializable]
[Table("users")]
public class User : IUserData
{
    [PrimaryKey]
    [Column("id")] 
    public int id { get; set; }

    [Ignore]
    public int user_id
    {
        get => id;
        set => id = value;
    }


    [Column("nickname")] public string nickname { get; set; }
    [Unique, Column("email")] public string email { get; set; }
    [Column("password")] public string password { get; set; }
    [Column("profile_picture")] public string profile_picture { get; set; }
    [Column("created_at")] public string created_at { get; set; }
    [Column("updated_at")] public string updated_at { get; set; }
    [Column("is_active")] public int is_active { get; set; }
    [Column("is_verified")] public int is_verified { get; set; }
    [Column("verification_code")] public string verification_code { get; set; }
    [Column("points")] public int points { get; set; } = 0;
    [Column("screentime_minutes")] public float? screentime_minutes { get; set; }
    [Column("breaks_taken")] public int? breaks_taken { get; set; }
    [Column("health_score")] public int? health_score { get; set; }
}

[Serializable]
[Table("user_settings")]
public class UserSettings : IUserData
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] public int id { get; set; }

    [Indexed, Column("user_id")] public int user_id { get; set; }
    [Column("nickname")] public string nickname { get; set; }
    [Column("password_hash")] public string password_hash { get; set; }
    [Column("sync_data_enabled")] public int sync_data_enabled { get; set; }
    [Column("reminder_interval_min")] public int reminder_interval_min { get; set; }
    
    [Column("survey_interval_hr")] public int survey_interval_hr { get; set; }
    [Column("eye_care_strategy")] public string eye_care_strategy { get; set; }
    [Column("updated_at")] public string updated_at { get; set; }


[Ignore]
public DateTime UpdatedAtDateTime
{
    get => DateTime.TryParse(updated_at, out var dt) ? dt : DateTime.MinValue;
    set => updated_at = value.ToString("yyyy-MM-dd HH:mm:ss");
}
}

[Serializable]
[Table("user_wardrobe")]
public class UserWardrobe : IUserData
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] public int id { get; set; }

    [Indexed, Column("user_id")] public int user_id { get; set; }
    [Column("item_id")] public int item_id { get; set; }
    [Column("is_equipped")] public bool is_equipped { get; set; }
    [Column("updated_at")] public string updated_at { get; set; }


[Ignore]
public DateTime UpdatedAtDateTime
{
    get => DateTime.TryParse(updated_at, out var dt) ? dt : DateTime.MinValue;
    set => updated_at = value.ToString("yyyy-MM-dd HH:mm:ss");
}

}

[Serializable]
[Table("user_achievements")]
public class UserAchievement : IUserData
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] 
    public int id { get; set; }

    [Indexed, Column("user_id")] 
    public int user_id { get; set; }

    [Column("achievement_id")] 
    public int achievement_id { get; set; }

    [Column("unlocked_at")] 
    public string unlocked_at { get; set; }

    [Column("claimed")] 
    public bool claimed { get; set; }

    [Column("synced")] 
    public bool synced { get; set; }

    // ADD THIS MISSING COLUMN
    [Column("updated_at")]
    public string updated_at { get; set; }

    [Ignore]
    public DateTime UpdatedAtDateTime
    {
        get => DateTime.TryParse(updated_at, out var dt) ? dt : DateTime.MinValue;
        set => updated_at = value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    [Ignore]
    public DateTime UnlockedAtDateTime
    {
        get => DateTime.TryParse(unlocked_at, out var dt) ? dt : DateTime.MinValue;
        set => unlocked_at = value.ToString("yyyy-MM-dd HH:mm:ss");
    }
}





// ===================================================
// Rewards / Wardrobe / Pets
// ===================================================




[Serializable]
[Table("wardrobe_items")]
public class WardrobeItem
{
    [PrimaryKey, AutoIncrement]
    [Column("item_id")] public int item_id { get; set; }
    [Column("name")] public string name { get; set; }
    [Column("description")] public string description { get; set; }
    [Column("type")] public string type { get; set; }
    [Column("price")] public decimal price { get; set; }
    [Column("image")] public byte[] image { get; set; } // BLOB now
    [Column("created_at")] public DateTime? created_at { get; set; }
}


// ===================================================
// Eye Health & Learning
// ===================================================
[Serializable]
[Table("eyehealth_log")]
public class EyeHealthLog : IUserData, IHasDate
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] public int id { get; set; }
    [Indexed, Column("user_id")] public int user_id { get; set; }

    [Column("log_time")] 
    public string date { get; set; } // store as string in SQLite

    [Ignore]
    public DateTime DateTimeValue
    {
        get => DateTime.TryParse(date, out var dt) ? dt : DateTime.MinValue;
        set => date = value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    [Column("screen_time_hours")] public double screen_time_hours { get; set; }
    [Column("symptoms")] public string symptoms { get; set; }
    [Column("pattern_detected")] public string pattern_detected { get; set; }
    [Column("ai_recommendation")] public string ai_recommendation { get; set; }
}

[Serializable]
[Table("eye_issues")]
public class EyeIssue
{
    [PrimaryKey, AutoIncrement]
    [Column("issue_id")] public int issue_id { get; set; }

    [Column("title")] public string title { get; set; }
    [Column("description")] public string description { get; set; }
    [Column("suggested_action")] public string suggested_action { get; set; }
    [Column("link")] public string link { get; set; }
    [Column("created_at")] public string created_at { get; set; }
}

[Serializable]
[Table("eye_trivias")]
public class EyeTrivia
{
    [PrimaryKey, AutoIncrement]
    [Column("trivia_id")] public int trivia_id { get; set; }

    [Column("title")] public string title { get; set; }
    [Column("description")] public string description { get; set; }
    [Column("link")] public string link { get; set; }
    [Column("created_at")] public string created_at { get; set; }
}

[Serializable]
[Table("eye_tips")]
public class EyeTip
{
    [PrimaryKey, AutoIncrement]
    [Column("tip_id")] public int tip_id { get; set; }

    [Column("title")] public string title { get; set; }
    [Column("content")] public string content { get; set; }
    [Column("link")] public string link { get; set; }
    [Column("created_at")] public string created_at { get; set; }
    [Column("category")] public string category { get; set; }
}

// ===================================================
// System / Utility
// ===================================================
[Serializable]
[Table("achievements")]
public class Achievement
{
    [PrimaryKey, AutoIncrement]
    [Column("achievement_id")] public int achievement_id { get; set; }

    [Column("title")] public string title { get; set; }
    [Column("description")] public string description { get; set; }
    [Column("points")] public int points { get; set; }
    [Column("requirement_type")] public string requirement_type { get; set; }
    [Column("requirement_value")] public int requirement_value { get; set; }
    [Column("created_at")] public string created_at { get; set; }
}

[Serializable]
[Table("screentime_history")]
public class ScreenTimeHistory : IUserData, IHasDate
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] 
    public int id { get; set; }

    [Indexed, Column("user_id")] 
    public int user_id { get; set; }
    
    [Indexed, Column("date")] 
    public string date { get; set; }
    
    [Column("screen_on_time")] 
    public float screen_on_time { get; set; }
    
    [Column("breaks_taken")] 
    public int breaks_taken { get; set; }
    
    [Column("eye_score")] 
    public int eye_score { get; set; }

    [Column("updated_at")] 
    public string updated_at { get; set; }

    [Ignore]
    public DateTime DateTimeValue
    {
        get
        {
            if (DateTime.TryParse(date, out var parsed))
                return parsed;
            return DateTime.MinValue;
        }
        set
        {
            date = value.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    [Ignore]
    public DateTime UpdatedAtDateTime
    {
        get
        {
            if (!string.IsNullOrEmpty(updated_at) && DateTime.TryParse(updated_at, out var parsed))
                return parsed;
            return DateTime.MinValue;
        }
        set
        {
            updated_at = value.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}

[System.Serializable]
public class AppUsageInfo
{
    public string id;
    public string packageName;
    public string appName;
    public long startTimeMillis;
    public long endTimeMillis;
    public long durationMillis;
    public string userId;
}






[Serializable]
[Table("feedbacks")]
public class Feedback : IUserData
{
    [PrimaryKey, AutoIncrement]
    [Column("feedback_id")] public int feedback_id { get; set; }

    [Indexed, Column("user_id")] public int user_id { get; set; }
    [Column("nickname")] public string name { get; set; }
    [Column("email")] public string email { get; set; }
    [Column("message")] public string message { get; set; }
    [Column("created_at")] public string created_at { get; set; }
}

[Serializable]
[Table("admin_history")]
public class AdminHistory
{
    [PrimaryKey, AutoIncrement]
    [Column("id")] public int id { get; set; }

    [Column("action")] public string action { get; set; }
    [Column("Description")] public string Description { get; set; }
    [Column("created_at")] public string created_at { get; set; }
    [Column("old_values")] public string old_values { get; set; }
    [Column("new_values")] public string new_values { get; set; }
    [Column("table_name")] public string table_name { get; set; }
    [Column("record_id")] public int? record_id { get; set; }
}