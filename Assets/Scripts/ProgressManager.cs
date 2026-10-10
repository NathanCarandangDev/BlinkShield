using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;

public class ProgressManager : MonoBehaviour
{
    public static ProgressManager Instance { get; private set; }

    [Header("DES Weekly Graph")]
    public RectTransform DESGraphContent;
    public GameObject desBarPrefab;
    public TMP_Text[] desDayLabels;
    public float maxDESHeight = 400f;

    [Header("Screen Weekly Graph")]
    public RectTransform ScreenGraphContent;
    public GameObject screenBarPrefab;
    public TMP_Text[] screenDayLabels;
    public float maxHours = 10f;
    public float maxBarHeight = 400f; // ← CHANGED TO 400

    [Header("Animation Settings")]
    public float barLerpSpeed = 3f;
    public float colorLerpSpeed = 3f;

    [Header("Bar Width Settings")]
    public float barWidthOffset = -15f;

    [Header("Bar Spacing Settings")]
    [Range(0f, 0.2f)]
    public float barSpacing = 0.005f;
    [Range(0f, 0.1f)]
    public float sideMargins = 0.02f;

    [Header("Weekly Stats Display")]
    public TMP_Text weeklyTotalText;
    public TMP_Text weeklyBreaksText;
    public TMP_Text desMostCommonSymptomText;

    [Header("Screen Graph Colors")]
    public Color[] gradientColors = new Color[]
    {
        new Color(0.2f, 0.4f, 0.8f, 1f),
        new Color(0.4f, 0.2f, 0.8f, 1f),
        new Color(0.8f, 0.2f, 0.6f, 1f),
        new Color(0.8f, 0.4f, 0.2f, 1f),
        new Color(0.8f, 0.2f, 0.2f, 1f)
    };

    // Screen Graph Data
    private RectTransform[] screenBarFills = new RectTransform[7];
    private Image[] screenBarFillImages = new Image[7];
    private GameObject activeScreenGraph;
    
    // DES Graph Data
    private RectTransform[] desBarFills = new RectTransform[7];
    private Image[] desBarFillImages = new Image[7];
    private GameObject activeDESGraph;
    
    // TRUTH
    private float[] targetBarHeights = new float[7];
    private Color[] targetBarColors = new Color[7];
    private float[] desTargetHeights = new float[7];
    private Color[] desTargetColors = new Color[7];
    
    // ANIMATION STATE
    private float[] currentBarHeights = new float[7];
    private Color[] currentBarColors = new Color[7];
    private float[] desCurrentHeights = new float[7];
    private Color[] desCurrentColors = new Color[7];
    
    // TODAY'S LIVE DATA
    private float liveTodayHours = 0f;
    private bool hasTodayLiveData = false;

    private readonly string[] dayOrder = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    private DateTime currentDate;
    private DateTime currentWeekStart;

    private int currentUserId = 0;
    private bool userLoggedIn = false;

    private Dictionary<string, Color> symptomColors = new Dictionary<string, Color>
    {
        { "dry_eyes", Color.red },
        { "blurry_vision", new Color(1f, 0.5f, 0f, 1f) },
        { "headache", Color.yellow },
        { "dizziness", Color.green },
        { "glare", Color.blue },
        { "none", new Color(0.5f, 0f, 0.5f, 1f) }
    };

    private Dictionary<string, string> symptomDisplayNames = new Dictionary<string, string>
    {
        { "dry_eyes", "Dry Eyes" },
        { "blurry_vision", "Blurry Vision" },
        { "headache", "Headache" },
        { "dizziness", "Dizziness" },
        { "glare", "Glare Sensitivity" },
        { "none", "No Symptoms" }
    };

    private int todayLiveBreaks = 0;
    private int todayEyeScore = 100;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        currentDate = DateTime.Today;
        currentWeekStart = GetStartOfWeek(DateTime.Today);
        
        if (ScreenTimeManager.Instance != null)
        {
            ScreenTimeManager.Instance.OnScreenTimeDataChanged += OnScreenTimeDataChanged;
            ScreenTimeManager.Instance.OnBreakTaken += OnBreakTaken;
        }
        
        SessionManager.OnSessionStarted += OnUserLogin;
        SessionManager.OnSessionEnded += OnUserLogout;
    }

    private void OnDestroy()
    {
        if (ScreenTimeManager.Instance != null)
        {
            ScreenTimeManager.Instance.OnScreenTimeDataChanged -= OnScreenTimeDataChanged;
            ScreenTimeManager.Instance.OnBreakTaken -= OnBreakTaken;
        }
        
        SessionManager.OnSessionStarted -= OnUserLogin;
        SessionManager.OnSessionEnded -= OnUserLogout;
    }

    private void Start()
    {
        if (SessionManager.Instance?.CurrentUser != null)
        {
            OnUserLogin(SessionManager.Instance.CurrentUser);
        }
        else
        {
            InitializeEmptyGraphs();
        }
    }

    private void Update()
    {
        DateTime today = DateTime.Today;
        
        if (today > currentDate)
        {
            ProcessDayChange();
        }

        DateTime weekStart = GetStartOfWeek(today);
        if (weekStart > currentWeekStart)
        {
            ProcessWeekChange(weekStart);
        }
        
        AnimateScreenBars();
        AnimateDESBars();
    }

    #region ANIMATION LOOP (USING fillAmount)
    private void AnimateScreenBars()
    {
        for (int i = 0; i < 7; i++)
        {
            if (screenBarFillImages[i] != null)
            {
                currentBarHeights[i] = Mathf.Lerp(currentBarHeights[i], targetBarHeights[i], Time.deltaTime * barLerpSpeed);
                float normalized = maxBarHeight > 0 ? currentBarHeights[i] / maxBarHeight : 0f;
                screenBarFillImages[i].fillAmount = normalized;
                
                currentBarColors[i] = Color.Lerp(currentBarColors[i], targetBarColors[i], Time.deltaTime * colorLerpSpeed);
                screenBarFillImages[i].color = currentBarColors[i];
            }
        }
    }
    
    private void AnimateDESBars()
    {
        for (int i = 0; i < 7; i++)
        {
            if (desBarFillImages[i] != null)
            {
                desCurrentHeights[i] = Mathf.Lerp(desCurrentHeights[i], desTargetHeights[i], Time.deltaTime * barLerpSpeed);
                float normalized = maxDESHeight > 0 ? desCurrentHeights[i] / maxDESHeight : 0f;
                desBarFillImages[i].fillAmount = normalized;
                
                desCurrentColors[i] = Color.Lerp(desCurrentColors[i], desTargetColors[i], Time.deltaTime * colorLerpSpeed);
                desBarFillImages[i].color = desCurrentColors[i];
            }
        }
    }
    #endregion

    #region EVENT HANDLERS
    private void OnScreenTimeDataChanged(int userId, float minutes, int breaks, int eyeScore)
    {
        if (!userLoggedIn || userId != currentUserId) return;
        
        liveTodayHours = minutes / 60f;
        hasTodayLiveData = true;
        todayLiveBreaks = breaks;
        todayEyeScore = eyeScore;
        
        DateTime today = DateTime.Today;
        DateTime startOfWeek = GetStartOfWeek(today);
        int todayIndex = (today - startOfWeek).Days;
        
        if (todayIndex >= 0 && todayIndex < 7)
        {
            targetBarHeights[todayIndex] = CalculateBarHeight(liveTodayHours);
            targetBarColors[todayIndex] = GetBarColorForHours(liveTodayHours);
        }
        
        UpdateScreenTimeStatsDisplay();
    }

    private void OnBreakTaken(int breaks)
    {
        if (!userLoggedIn) return;
        todayLiveBreaks = breaks;
        UpdateScreenTimeStatsDisplay();
    }

    private void ProcessDayChange()
    {
        currentDate = DateTime.Today;
        liveTodayHours = 0f;
        hasTodayLiveData = false;
        todayLiveBreaks = 0;
        todayEyeScore = 100;
        UpdateAllGraphs();
    }

    private void ProcessWeekChange(DateTime newWeekStart)
    {
        currentWeekStart = newWeekStart;
        for (int i = 0; i < 7; i++)
        {
            targetBarHeights[i] = 0f;
            targetBarColors[i] = gradientColors[0];
            desTargetHeights[i] = 0f;
            desTargetColors[i] = Color.white;
        }
        liveTodayHours = 0f;
        hasTodayLiveData = false;
        UpdateAllGraphs();
    }
    #endregion

    #region GRAPH MANAGEMENT
    public void UpdateAllGraphs()
    {
        if (!userLoggedIn) return;
        UpdateWeeklyScreenGraph();
        UpdateWeeklyDESGraph();
        UpdateDESStatsDisplay();
    }

    public void UpdateWeeklyScreenGraph()
    {
        if (!userLoggedIn || activeScreenGraph == null) return;

        DateTime today = DateTime.Today;
        DateTime startOfWeek = GetStartOfWeek(today);

        for (int i = 0; i < 7; i++)
        {
            DateTime graphDay = startOfWeek.AddDays(i);
            bool isToday = graphDay.Date == today.Date;
            bool isFuture = graphDay.Date > today.Date;
            bool isPast = graphDay.Date < today.Date;
            
            if (screenBarFillImages[i] == null) continue;
            
            if (isFuture)
            {
                screenBarFillImages[i].gameObject.SetActive(false);
                targetBarHeights[i] = 0f;
                targetBarColors[i] = gradientColors[0];
                continue;
            }
            
            screenBarFillImages[i].gameObject.SetActive(true);
            
            if (isPast)
            {
                float hours = GetDatabaseScreenTimeForDate(graphDay);
                targetBarHeights[i] = CalculateBarHeight(hours);
                targetBarColors[i] = GetBarColorForHours(hours);
            }
            else if (isToday)
            {
                targetBarHeights[i] = CalculateBarHeight(liveTodayHours);
                targetBarColors[i] = GetBarColorForHours(liveTodayHours);
            }
            
            if (screenDayLabels != null && i < screenDayLabels.Length)
            {
                screenDayLabels[i].text = dayOrder[i];
            }
        }
        
        activeScreenGraph.SetActive(true);
        UpdateScreenTimeStatsDisplay();
    }

    public void UpdateWeeklyDESGraph()
    {
        if (!userLoggedIn) return;
        if (activeDESGraph == null) InitializeDESGraph();
        if (activeDESGraph == null) return;

        activeDESGraph.SetActive(true);
        DateTime startOfWeek = GetStartOfWeek(DateTime.Today);

        for (int i = 0; i < 7; i++)
        {
            DateTime day = startOfWeek.AddDays(i);
            List<EyeHealthLog> records = DatabaseManager.Instance != null
                ? DatabaseManager.Instance.GetRange<EyeHealthLog>(day, day) ?? new List<EyeHealthLog>()
                : new List<EyeHealthLog>();
            
            ProcessDESDayData(i, day, records);
        }

        UpdateDESDayLabels();
        UpdateDESStatsDisplay();
    }

    private bool ProcessDESDayData(int dayIndex, DateTime day, List<EyeHealthLog> records)
    {
        if (dayIndex < 0 || dayIndex >= 7) return false;
        
        if (records != null && records.Count > 0)
        {
            Dictionary<string, int> symptomCounts = CountSymptoms(records);
            int totalSymptoms = symptomCounts.Values.Sum();
            
            if (totalSymptoms > 0)
            {
                float symptomHeight = Mathf.Clamp((float)totalSymptoms * (maxDESHeight / 10f), 10f, maxDESHeight);
                Color symptomColor = GetDESBarColor(symptomCounts);
                desTargetHeights[dayIndex] = symptomHeight;
                desTargetColors[dayIndex] = symptomColor;
            }
            else
            {
                desTargetHeights[dayIndex] = 2f;
                desTargetColors[dayIndex] = symptomColors["none"];
            }
        }
        else
        {
            desTargetHeights[dayIndex] = 0f;
            desTargetColors[dayIndex] = Color.white;
        }

        if (desBarFillImages[dayIndex] != null)
        {
            bool visible = desTargetHeights[dayIndex] > 0;
            desBarFillImages[dayIndex].gameObject.SetActive(visible);
        }

        return records?.Count > 0;
    }
    #endregion

    #region HELPERS
    private DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private float GetDatabaseScreenTimeForDate(DateTime date)
    {
        if (!userLoggedIn || DatabaseManager.Instance == null) return 0f;
        try
        {
            var records = DatabaseManager.Instance.GetRange<ScreenTimeHistory>(date, date);
            return records?.FirstOrDefault()?.screen_on_time / 60f ?? 0f;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ProgressManager] Failed to get screen time for {date:yyyy-MM-dd}: {ex.Message}");
        }
        return 0f;
    }

    private float CalculateBarHeight(float hours)
    {
        if (hours <= 0) return 0f;
        float height = (hours / maxHours) * maxBarHeight;
        return Mathf.Clamp(height, 5f, maxBarHeight);
    }

    private Color GetBarColorForHours(float hours)
    {
        float t = Mathf.Clamp01(hours / maxHours);
        if (gradientColors.Length == 0) return Color.white;
        if (gradientColors.Length == 1) return gradientColors[0];

        float colorIndex = t * (gradientColors.Length - 1);
        int i1 = Mathf.FloorToInt(colorIndex);
        int i2 = Mathf.Min(i1 + 1, gradientColors.Length - 1);
        float blend = colorIndex - i1;
        return Color.Lerp(gradientColors[i1], gradientColors[i2], blend);
    }

    private Dictionary<string, int> CountSymptoms(List<EyeHealthLog> records)
    {
        var counts = symptomColors.Keys.Where(k => k != "none").ToDictionary(k => k, k => 0);
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.symptoms)) continue;
            foreach (string symptom in record.symptoms.Split(','))
            {
                string key = symptom.Trim().ToLower();
                if (key != "none" && counts.ContainsKey(key)) counts[key]++;
            }
        }
        return counts;
    }

    private Color GetDESBarColor(Dictionary<string, int> symptomCounts)
    {
        string mostCommon = GetMostCommonSymptom(symptomCounts);
        return symptomColors.GetValueOrDefault(mostCommon, new Color(0.5f, 0.5f, 0.5f, 1f));
    }

    private string GetMostCommonSymptom(Dictionary<string, int> symptomCounts)
    {
        if (symptomCounts == null || symptomCounts.Values.All(v => v == 0)) return "none";
        return symptomCounts.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
    }
    #endregion

    #region INITIALIZATION
    private void InitializeScreenGraph()
    {
        if (ScreenGraphContent == null || screenBarPrefab == null) return;
        if (activeScreenGraph != null) Destroy(activeScreenGraph);

        activeScreenGraph = Instantiate(screenBarPrefab, ScreenGraphContent);
        if (activeScreenGraph == null) return;

        var graphRT = activeScreenGraph.GetComponent<RectTransform>();
        if (graphRT != null)
        {
            graphRT.anchorMin = Vector2.zero;
            graphRT.anchorMax = Vector2.one;
            graphRT.anchoredPosition = Vector2.zero;
            graphRT.sizeDelta = Vector2.zero;
        }

        CacheScreenBarFills();
        for (int i = 0; i < 7; i++)
        {
            targetBarHeights[i] = currentBarHeights[i] = 0f;
            targetBarColors[i] = currentBarColors[i] = gradientColors[0];
        }
        
        activeScreenGraph.SetActive(userLoggedIn);
    }

    private void CacheScreenBarFills()
    {
        if (activeScreenGraph == null) return;

        screenBarFills = new RectTransform[7];
        screenBarFillImages = new Image[7];

        for (int i = 0; i < 7; i++)
        {
            string barName = $"Bar{i + 1}";
            Transform bar = activeScreenGraph.transform.Find(barName);
            if (bar == null)
            {
                var fallbacks = new[] { $"Bar {i + 1}", $"bar{i + 1}", $"Day{i + 1}", $"Day {i + 1}" };
                bar = fallbacks.Select(n => activeScreenGraph.transform.Find(n)).FirstOrDefault(t => t != null);
            }
            if (bar == null) continue;

            var barRT = bar.GetComponent<RectTransform>();
            if (barRT != null)
            {
                float barWidth = (1f - 2f * sideMargins - 6f * barSpacing) / 7f;
                float startPos = sideMargins;
                float barStart = startPos + i * (barWidth + barSpacing);
                float barEnd = barStart + barWidth;
                
                barRT.anchorMin = new Vector2(barStart, 0);
                barRT.anchorMax = new Vector2(barEnd, 0);
                barRT.anchoredPosition = Vector2.zero;
                barRT.sizeDelta = new Vector2(barWidthOffset, maxBarHeight); // ← 400px
            }

            Transform fill = bar.Find("Fill");
            if (fill == null)
            {
                var fillNames = new[] { "fill", "BarFill", "bar_fill", "FillArea" };
                fill = fillNames.Select(n => bar.Find(n)).FirstOrDefault(t => t != null);
            }

            if (fill != null)
            {
                RectTransform fillRT = fill.GetComponent<RectTransform>();
                if (fillRT != null)
                {
                    fillRT.anchorMin = new Vector2(0, 0);
                    fillRT.anchorMax = new Vector2(1, 0);
                    fillRT.pivot = new Vector2(0.5f, 0);
                    fillRT.anchoredPosition = Vector2.zero;
                    fillRT.sizeDelta = new Vector2(0, maxBarHeight); // ← CRITICAL: 400px

                    screenBarFills[i] = fillRT;
                    
                    Image fillImage = fillRT.GetComponent<Image>();
                    if (fillImage == null) fillImage = fillRT.gameObject.AddComponent<Image>();
                    fillImage.type = Image.Type.Filled;
                    fillImage.fillMethod = Image.FillMethod.Vertical;
                    fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                    fillImage.fillAmount = 0f;
                    fillImage.raycastTarget = false;
                    // Ensure fill is visible
                    fillImage.color = new Color(gradientColors[0].r, gradientColors[0].g, gradientColors[0].b, 1f);

                    screenBarFillImages[i] = fillImage;
                }
            }
            else
            {
                Debug.LogWarning($"[ProgressManager] Fill not found for Screen Bar {i + 1}");
            }
        }
    }

    private void InitializeDESGraph()
    {
        if (DESGraphContent == null || desBarPrefab == null) return;
        if (activeDESGraph != null) Destroy(activeDESGraph);

        activeDESGraph = Instantiate(desBarPrefab, DESGraphContent);
        if (activeDESGraph == null) return;

        var graphRT = activeDESGraph.GetComponent<RectTransform>();
        if (graphRT != null)
        {
            graphRT.anchorMin = Vector2.zero;
            graphRT.anchorMax = Vector2.one;
            graphRT.anchoredPosition = Vector2.zero;
            graphRT.sizeDelta = Vector2.zero;
        }

        CacheDESBarFills();
        for (int i = 0; i < 7; i++)
        {
            desTargetHeights[i] = desCurrentHeights[i] = 0f;
            desTargetColors[i] = desCurrentColors[i] = Color.white;
        }
    }

    private void CacheDESBarFills()
    {
        if (activeDESGraph == null) return;

        desBarFills = new RectTransform[7];
        desBarFillImages = new Image[7];

        for (int i = 0; i < 7; i++)
        {
            string barName = $"Bar{i + 1}";
            Transform bar = activeDESGraph.transform.Find(barName);
            if (bar == null)
            {
                var fallbacks = new[] { $"Bar {i + 1}", $"bar{i + 1}", $"Day{i + 1}", $"Day {i + 1}" };
                bar = fallbacks.Select(n => activeDESGraph.transform.Find(n)).FirstOrDefault(t => t != null);
            }
            if (bar == null) continue;

            var barRT = bar.GetComponent<RectTransform>();
            if (barRT != null)
            {
                float barWidth = (1f - 2f * sideMargins - 6f * barSpacing) / 7f;
                float startPos = sideMargins;
                float barStart = startPos + i * (barWidth + barSpacing);
                float barEnd = barStart + barWidth;
                
                barRT.anchorMin = new Vector2(barStart, 0);
                barRT.anchorMax = new Vector2(barEnd, 0);
                barRT.anchoredPosition = Vector2.zero;
                barRT.sizeDelta = new Vector2(barWidthOffset, maxDESHeight); // ← 400px
            }

            Transform fill = bar.Find("Fill");
            if (fill == null)
            {
                var fillNames = new[] { "fill", "BarFill", "bar_fill", "FillArea", "FillArea/Fill" };
                fill = fillNames.Select(n => bar.Find(n)).FirstOrDefault(t => t != null);
            }

            if (fill != null)
            {
                RectTransform fillRT = fill.GetComponent<RectTransform>();
                if (fillRT != null)
                {
                    fillRT.anchorMin = new Vector2(0, 0);
                    fillRT.anchorMax = new Vector2(1, 0);
                    fillRT.pivot = new Vector2(0.5f, 0);
                    fillRT.anchoredPosition = Vector2.zero;
                    fillRT.sizeDelta = new Vector2(0, maxDESHeight); // ← CRITICAL: 400px

                    desBarFills[i] = fillRT;
                    
                    Image fillImage = fillRT.GetComponent<Image>();
                    if (fillImage == null) fillImage = fillRT.gameObject.AddComponent<Image>();
                    fillImage.type = Image.Type.Filled;
                    fillImage.fillMethod = Image.FillMethod.Vertical;
                    fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                    fillImage.fillAmount = 0f;
                    fillImage.raycastTarget = false;
                    fillImage.color = Color.white;

                    desBarFillImages[i] = fillImage;
                }
            }
            else
            {
                Debug.LogWarning($"[ProgressManager] Fill not found for DES Bar {i + 1}");
            }
        }
    }
    #endregion

    #region USER MANAGEMENT
    private void OnUserLogin(User user)
    {
        if (user == null) return;
        currentUserId = user.id;
        userLoggedIn = true;
        InitializeScreenGraph();
        InitializeDESGraph();
        UpdateAllGraphs();
    }

    private void OnUserLogout()
    {
        ClearAllGraphs();
        ResetAllStats();
        currentUserId = 0;
        userLoggedIn = false;
        hasTodayLiveData = false;
        liveTodayHours = 0f;
        InitializeEmptyGraphs();
    }
    #endregion

    #region STATS DISPLAY
    public void UpdateScreenTimeStatsDisplay()
    {
        if (weeklyTotalText == null && weeklyBreaksText == null) return;
        if (!userLoggedIn)
        {
            if (weeklyTotalText != null) weeklyTotalText.text = "No data";
            if (weeklyBreaksText != null) weeklyBreaksText.text = "No data";
            return;
        }

        DateTime startOfWeek = GetStartOfWeek(DateTime.Today);
        DateTime today = DateTime.Today;
        
        int totalMinutes = 0, totalBreaks = 0;
        for (int i = 0; i < 7; i++)
        {
            DateTime day = startOfWeek.AddDays(i);
            if (day.Date > today.Date) break;
            
            float hours = day.Date == today.Date ? liveTodayHours : GetDatabaseScreenTimeForDate(day);
            int breaks = day.Date == today.Date ? todayLiveBreaks : 
                (DatabaseManager.Instance?.GetRange<ScreenTimeHistory>(day, day)?.FirstOrDefault()?.breaks_taken ?? 0);

            if (hours > 0)
            {
                totalMinutes += Mathf.RoundToInt(hours * 60f);
                totalBreaks += breaks;
            }
        }

        if (weeklyTotalText != null)
        {
            weeklyTotalText.text = $"Total: {totalMinutes / 60}h {totalMinutes % 60}m";
        }
        if (weeklyBreaksText != null)
        {
            weeklyBreaksText.text = $"Breaks: {totalBreaks}";
        }
    }

    public void UpdateDESStatsDisplay()
    {
        if (desMostCommonSymptomText == null) return;
        if (!userLoggedIn || DatabaseManager.Instance == null)
        {
            desMostCommonSymptomText.text = "No symptom data";
            return;
        }

        DateTime startOfWeek = GetStartOfWeek(DateTime.Today);
        var records = DatabaseManager.Instance.GetRange<EyeHealthLog>(startOfWeek, startOfWeek.AddDays(6)) ?? new List<EyeHealthLog>();

        if (records.Count == 0)
        {
            desMostCommonSymptomText.text = "No symptoms reported this week";
            return;
        }

        string mostCommon = FindMostCommonSymptom(records);
        if (mostCommon == "No symptoms")
        {
            desMostCommonSymptomText.text = "No symptoms reported this week";
        }
        else
        {
            string displayName = symptomDisplayNames.GetValueOrDefault(mostCommon, mostCommon);
            desMostCommonSymptomText.text = $"Most common: {displayName}";
        }
    }

    private string FindMostCommonSymptom(List<EyeHealthLog> records)
    {
        var counts = new Dictionary<string, int>();
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.symptoms)) continue;
            foreach (var symptom in record.symptoms.Split(','))
            {
                string key = symptom.Trim().ToLower();
                if (key != "none" && !string.IsNullOrEmpty(key))
                    counts[key] = counts.GetValueOrDefault(key, 0) + 1;
            }
        }
        return counts.Count == 0 ? "No symptoms" : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    private void UpdateDESDayLabels()
    {
        if (desDayLabels == null || desDayLabels.Length != 7) return;
        for (int i = 0; i < 7; i++)
        {
            desDayLabels[i]?.SetText(dayOrder[i]);
        }
    }
    #endregion

    #region CLEANUP
    private void ClearAllGraphs()
    {
        if (activeScreenGraph) activeScreenGraph.SetActive(false);
        if (activeDESGraph) activeDESGraph.SetActive(false);
    }

    private void InitializeEmptyGraphs()
    {
        ClearAllGraphs();
        ResetAllStats();
    }

    private void ResetAllStats()
    {
        if (weeklyTotalText) weeklyTotalText.text = "No data";
        if (weeklyBreaksText) weeklyBreaksText.text = "No data";
        if (desMostCommonSymptomText) desMostCommonSymptomText.text = "No symptom data";
    }
    #endregion

    #region PUBLIC METHODS
    public void RefreshWeeklyData()
    {
        if (userLoggedIn) UpdateAllGraphs();
    }

    [ContextMenu("Force Refresh Graphs")]
    public void ForceRefreshGraphs()
    {
        UpdateAllGraphs();
    }
    #endregion
}