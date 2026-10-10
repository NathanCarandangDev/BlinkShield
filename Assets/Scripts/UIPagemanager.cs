using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// PURE NAVIGATION manager for showing and hiding UI pages and overlays.
/// Does NOT decide which page to show initially - only provides navigation services.
/// </summary>
public class UIManager : MonoBehaviour
{
    // 🔹 Singleton instance
    public static UIManager Instance { get; private set; }

    [Header("Main full-screen pages (auto-collected)")]
    [Tooltip("Full-screen panels that replace each other (only one active at a time).")]
    public GameObject[] allPages;

    [Header("Pop-up overlay panels (stacked, do not replace main pages)")]
    [Tooltip("Overlays like dialogs, pop-ups, tooltips that sit above main pages.")]
    public GameObject[] overlayPanels;

    [Header("Special pages")]
    [Tooltip("Login page reference - NO AUTO-SHOWING.")]
    public GameObject loginPage;

    [Tooltip("Signup page (optional, prevents showing as history).")]
    public GameObject signupPage;

    [Header("Content Popups")]
    [SerializeField] private GameObject pageTipPopup;    // For eye tips
    [SerializeField] private GameObject pageIssuePopup;  // For eye issues
    [SerializeField] private GameObject pageTriviaPopup; // For eye trivia

    private readonly Stack<GameObject> history = new Stack<GameObject>();        // main page history
    private readonly Stack<GameObject> overlayHistory = new Stack<GameObject>(); // overlay stack
    private GameObject currentPage;

    #region Unity Lifecycle
    private void Awake()
    {
        // ✅ Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // persists between scenes
        }
        else
        {
            Destroy(gameObject); // prevent duplicates
            return;
        }

        // ✅ Auto-collect all direct child pages if none manually assigned
        if (allPages == null || allPages.Length == 0)
        {
            List<GameObject> pages = new List<GameObject>();
            foreach (Transform child in transform)
                pages.Add(child.gameObject);

            allPages = pages.ToArray();
            Debug.Log($"[UIManager] Auto-registered {allPages.Length} pages.");
        }

        // ✅ Ensure login/signup are included (for reference only)
        EnsurePageRegistered(loginPage);
        EnsurePageRegistered(signupPage);
        
        // 🔹 CRITICAL FIX: Hide login page IMMEDIATELY if it's active
        ForceHideLoginPageOnAwake();

        // ✅ Ensure popup references are set
        EnsurePopupReference();
        EnsureIssuePopupReference();
        EnsureTriviaPopupReference();
        
        Debug.Log("[UIManager] Awake complete - Pure navigation manager ready");
    }

    // NEW: Force hide login page immediately on Awake
    private void ForceHideLoginPageOnAwake()
    {
        if (loginPage != null && loginPage.activeSelf)
        {
            Debug.Log($"[UIManager] 🔥 HIDING LOGIN PAGE ON AWAKE (was active)");
            loginPage.SetActive(false);
        }
        
        // Also check all pages and ensure only welcome-like pages are active
        GameObject welcomePage = null;
        foreach (GameObject page in allPages)
        {
            if (page != null && page.activeSelf)
            {
                // Check if this looks like a welcome page
                if (page.name.ToLower().Contains("welcome"))
                {
                    welcomePage = page;
                    Debug.Log($"[UIManager] Found welcome page: {page.name}");
                }
                // If it's login/signup, hide it
                else if (page == loginPage || page == signupPage || 
                        page.name.ToLower().Contains("login") || 
                        page.name.ToLower().Contains("sign"))
                {
                    Debug.Log($"[UIManager] Hiding non-welcome page on Awake: {page.name}");
                    page.SetActive(false);
                }
            }
        }
        
        // If no welcome page found but we have a login page active, that's a problem
        if (welcomePage == null && loginPage != null && loginPage.activeSelf)
        {
            Debug.LogError($"[UIManager] ❌ Login page active but no welcome page found!");
        }
    }

    private void Start()
    {
        // 🔹 Wait for SessionManager to initialize (for reference only)
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        Debug.Log("[UIManager] Starting delayed initialization");
        
        // Wait for SessionManager to be ready (optional)
        yield return new WaitUntil(() => SessionManager.Instance != null);
        
        Debug.Log("[UIManager] Navigation manager ready - No pages shown automatically");
        
        // 🔹 Find welcome page and ensure it's the only one active
        EnsureWelcomePageIsVisible();
        
        // Find current active page (if any is already active)
        bool foundActivePage = false;
        foreach (GameObject page in allPages)
        {
            if (page != null && page.activeSelf)
            {
                // Skip login/signup pages
                if (page == loginPage || page == signupPage || 
                    page.name.ToLower().Contains("login") || 
                    page.name.ToLower().Contains("sign"))
                {
                    Debug.Log($"[UIManager] ⚠️ Found login/signup page active: {page.name} - hiding it");
                    page.SetActive(false);
                    continue;
                }
                
                currentPage = page;
                Debug.Log($"[UIManager] ✅ Current page set to: {page.name}");
                foundActivePage = true;
                break;
            }
        }
        
        if (!foundActivePage)
        {
            Debug.Log($"[UIManager] ℹ️ No active page found - SessionManager will handle this");
        }
    }
    
    // NEW: Ensure welcome page is visible and others are hidden
    private void EnsureWelcomePageIsVisible()
    {
        Debug.Log("[UIManager] === ENSURING WELCOME PAGE VISIBLE ===");
        
        GameObject welcomePage = null;
        List<GameObject> pagesToHide = new List<GameObject>();
        
        // First pass: find welcome page and identify pages to hide
        foreach (GameObject page in allPages)
        {
            if (page == null) continue;
            
            if (page.activeSelf)
            {
                Debug.Log($"[UIManager] Page active: {page.name}");
                
                // Check if this is a welcome page
                if (page.name.ToLower().Contains("welcome"))
                {
                    welcomePage = page;
                    Debug.Log($"[UIManager] ✅ Found welcome page: {page.name}");
                }
                // Check if this should be hidden
                else if (page == loginPage || page == signupPage || 
                        page.name.ToLower().Contains("login") || 
                        page.name.ToLower().Contains("sign") ||
                        page.name.ToLower().Contains("home"))
                {
                    pagesToHide.Add(page);
                    Debug.Log($"[UIManager] Will hide: {page.name}");
                }
            }
        }
        
        // Hide unwanted pages
        foreach (GameObject page in pagesToHide)
        {
            page.SetActive(false);
            Debug.Log($"[UIManager] 🔧 Hid page: {page.name}");
        }
        
        // If no welcome page found, try to find one
        if (welcomePage == null)
        {
            welcomePage = FindPageContaining("welcome");
            if (welcomePage != null)
            {
                Debug.Log($"[UIManager] 🔍 Found welcome page by search: {welcomePage.name}");
                welcomePage.SetActive(true);
            }
        }
        
        // Final check: ensure login page is definitely hidden
        if (loginPage != null && loginPage.activeSelf)
        {
            Debug.LogError($"[UIManager] ❌ LOGIN PAGE STILL ACTIVE AFTER CLEANUP!");
            loginPage.SetActive(false);
        }
        
        Debug.Log($"[UIManager] === END WELCOME PAGE CHECK ===");
    }

    #endregion

    #region Helpers
    private void EnsurePageRegistered(GameObject page)
    {
        if (page == null) return;

        bool exists = System.Array.Exists(allPages, p => p == page);
        if (!exists)
        {
            var list = new List<GameObject>(allPages) { page };
            allPages = list.ToArray();
            Debug.Log($"[UIManager] Added missing page {page.name} to allPages.");
        }
    }

    private void HideAllPages()
    {
        foreach (GameObject page in allPages)
            if (page != null) page.SetActive(false);
    }

    private void EnsurePopupReference()
    {
        // If already assigned, we're good
        if (pageTipPopup != null)
        {
            Debug.Log("✅ pageTipPopup reference is already assigned");
            return;
        }

        Debug.Log("🔍 Searching for pageTipPopup...");
        
        // Method 1: First try to find it inside pageTips
        GameObject pageTipsContainer = GameObject.Find("pageTips");
        if (pageTipsContainer != null)
        {
            Transform popupTransform = pageTipsContainer.transform.Find("pageTipPopup");
            if (popupTransform != null)
            {
                pageTipPopup = popupTransform.gameObject;
                Debug.Log($"✅ Found pageTipPopup inside pageTips: {pageTipPopup.name}");
                return;
            }
        }

        // Method 2: Recursive search through entire scene
        pageTipPopup = FindObjectByNameRecursive("pageTipPopup");
        if (pageTipPopup != null)
        {
            Debug.Log($"✅ Found pageTipPopup via recursive search: {pageTipPopup.name}");
            if (pageTipPopup.transform.parent != null)
            {
                Debug.Log($"✅ Parent: {pageTipPopup.transform.parent.name}");
            }
            return;
        }

        // Method 3: Traditional search (as fallback)
        pageTipPopup = GameObject.Find("pageTipPopup");
        if (pageTipPopup != null)
        {
            Debug.Log("✅ Found pageTipPopup via GameObject.Find");
            return;
        }

        Debug.LogError("❌ Could not find pageTipPopup GameObject in scene!");
    }

    private void EnsureIssuePopupReference()
    {
        // If already assigned, we're good
        if (pageIssuePopup != null)
        {
            Debug.Log("✅ pageIssuePopup reference is already assigned");
            return;
        }

        Debug.Log("🔍 Searching for pageIssuePopup...");
        
        // Method 1: First try to find it inside pageIssues or similar container
        GameObject pageIssuesContainer = GameObject.Find("pageIssues");
        if (pageIssuesContainer != null)
        {
            Transform popupTransform = pageIssuesContainer.transform.Find("pageIssuePopup");
            if (popupTransform != null)
            {
                pageIssuePopup = popupTransform.gameObject;
                Debug.Log($"✅ Found pageIssuePopup inside pageIssues: {pageIssuePopup.name}");
                return;
            }
        }

        // Method 2: Recursive search through entire scene
        pageIssuePopup = FindObjectByNameRecursive("pageIssuePopup");
        if (pageIssuePopup != null)
        {
            Debug.Log($"✅ Found pageIssuePopup via recursive search: {pageIssuePopup.name}");
            if (pageIssuePopup.transform.parent != null)
            {
                Debug.Log($"✅ Parent: {pageIssuePopup.transform.parent.name}");
            }
            return;
        }

        // Method 3: Traditional search (as fallback)
        pageIssuePopup = GameObject.Find("pageIssuePopup");
        if (pageIssuePopup != null)
        {
            Debug.Log("✅ Found pageIssuePopup via GameObject.Find");
            return;
        }

        Debug.LogWarning("⚠️ Could not find pageIssuePopup GameObject in scene - will use tip popup as fallback");
    }

    private void EnsureTriviaPopupReference()
    {
        // If already assigned, we're good
        if (pageTriviaPopup != null)
        {
            Debug.Log("✅ pageTriviaPopup reference is already assigned");
            return;
        }

        Debug.Log("🔍 Searching for pageTriviaPopup...");
        
        // Method 1: First try to find it inside pageTrivias or similar container
        GameObject pageTriviasContainer = GameObject.Find("pageTrivias");
        if (pageTriviasContainer != null)
        {
            Transform popupTransform = pageTriviasContainer.transform.Find("pageTriviaPopup");
            if (popupTransform != null)
            {
                pageTriviaPopup = popupTransform.gameObject;
                Debug.Log($"✅ Found pageTriviaPopup inside pageTrivias: {pageTriviaPopup.name}");
                return;
            }
        }

        // Method 2: Recursive search through entire scene
        pageTriviaPopup = FindObjectByNameRecursive("pageTriviaPopup");
        if (pageTriviaPopup != null)
        {
            Debug.Log($"✅ Found pageTriviaPopup via recursive search: {pageTriviaPopup.name}");
            if (pageTriviaPopup.transform.parent != null)
            {
                Debug.Log($"✅ Parent: {pageTriviaPopup.transform.parent.name}");
            }
            return;
        }

        // Method 3: Traditional search (as fallback)
        pageTriviaPopup = GameObject.Find("pageTriviaPopup");
        if (pageTriviaPopup != null)
        {
            Debug.Log("✅ Found pageTriviaPopup via GameObject.Find");
            return;
        }

        Debug.LogWarning("⚠️ Could not find pageTriviaPopup GameObject in scene - will use tip popup as fallback");
    }

    // Recursive search through all children and nested children
    private GameObject FindObjectByNameRecursive(string name)
    {
        // Search from scene root
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject rootObject in rootObjects)
        {
            GameObject found = FindObjectByNameRecursive(rootObject.transform, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private GameObject FindObjectByNameRecursive(Transform parent, string name)
    {
        // Check all direct children
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child.gameObject;
            }
            
            // Recursively check children of this child
            GameObject found = FindObjectByNameRecursive(child, name);
            if (found != null)
            {
                return found;
            }
        }
        
        return null;
    }
    #endregion

    #region Main Page Handling
    public void ShowPage(GameObject targetPage)
    {
        if (targetPage == null)
        {
            Debug.LogError("[UIManager] Tried to show a null page!");
            return;
        }

        Debug.Log($"[UIManager] ShowPage called for: {targetPage.name}");
        Debug.Log($"[UIManager] Current page: {currentPage?.name ?? "null"}");

        // Add to history (but not login/signup or same page)
        if (currentPage != null &&
            currentPage != targetPage &&
            currentPage != loginPage &&
            currentPage != signupPage)
        {
            history.Push(currentPage);
            Debug.Log($"[UIManager] Added {currentPage.name} to history");
        }

        HideAllPages();

        targetPage.SetActive(true);
        currentPage = targetPage;
        Debug.Log($"[UIManager] Now showing: {currentPage.name}");
        
        // Debug log history count
        Debug.Log($"[UIManager] History count: {history.Count}");
    }

    public void ShowMainPage(GameObject targetPage)
    {
        if (targetPage == null)
        {
            Debug.LogError("[UIManager] ShowMainPage called with null target page!");
            return;
        }

        Debug.Log($"[UIManager] 🎯 ShowMainPage called for: {targetPage.name}");
        Debug.Log($"[UIManager] Current page: {currentPage?.name ?? "null"}");
        Debug.Log($"[UIManager] Clearing history (count: {history.Count})");
        
        // Ensure the page is registered
        EnsurePageRegistered(targetPage);
        
        history.Clear();
        ShowPage(targetPage);
    }

    public void GoBack()
    {
        Debug.Log($"[UIManager] GoBack called");
        Debug.Log($"[UIManager] Overlay count: {overlayHistory.Count}, History count: {history.Count}");

        // Close overlay first
        if (overlayHistory.Count > 0)
        {
            GameObject topOverlay = overlayHistory.Pop();
            if (topOverlay != null)
            {
                Debug.Log($"[UIManager] Closing overlay: {topOverlay.name}");
                topOverlay.SetActive(false);
            }
            return;
        }

        if (history.Count > 0)
        {
            GameObject previousPage = history.Pop();

            // Skip login/signup in back navigation
            while ((previousPage == loginPage || previousPage == signupPage) && history.Count > 0)
                previousPage = history.Pop();

            HideAllPages();

            if (previousPage != null)
            {
                Debug.Log($"[UIManager] Going back to: {previousPage.name}");
                previousPage.SetActive(true);
                currentPage = previousPage;
            }
        }
        else
        {
            Debug.Log("[UIManager] No history to go back to");
        }
    }
    #endregion

    #region Overlay Handling
    public void ShowOverlay(GameObject overlay)
    {
        Debug.Log($"🎯 ShowOverlay called for: {overlay?.name}");
        
        if (overlay == null)
        {
            Debug.LogError("❌ [UIManager] Tried to show a null overlay!");
            return;
        }

        Debug.Log($"✅ Activating overlay: {overlay.name}");
        Debug.Log($"✅ Overlay active before: {overlay.activeInHierarchy}");
        
        // Force activation and bring to front
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling(); // Bring to front
        
        // Check parent hierarchy visibility
        CheckParentHierarchy(overlay.transform);
        
        // Check UI components
        CheckUIComponents(overlay);
        
        overlayHistory.Push(overlay);
        
        Debug.Log($"✅ Overlay active after: {overlay.activeInHierarchy}");
        Debug.Log($"✅ Overlay stack count: {overlayHistory.Count}");
        
        // Force canvas update
        Canvas.ForceUpdateCanvases();
    }

    private void CheckParentHierarchy(Transform objTransform)
    {
        Debug.Log("🔍 Checking parent hierarchy:");
        Transform current = objTransform;
        while (current != null)
        {
            Debug.Log($"   - {current.name}: Active={current.gameObject.activeInHierarchy}, ActiveSelf={current.gameObject.activeSelf}");
            current = current.parent;
        }
    }

    private void CheckUIComponents(GameObject overlay)
    {
        Debug.Log("🔍 Checking UI components:");
        
        // Check Canvas
        Canvas canvas = overlay.GetComponent<Canvas>();
        if (canvas != null)
        {
            Debug.Log($"   - Canvas: enabled={canvas.enabled}, sortOrder={canvas.sortingOrder}");
        }
        else
        {
            Debug.Log("   - No Canvas component found");
        }
        
        // Check Canvas Group
        CanvasGroup canvasGroup = overlay.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            Debug.Log($"   - CanvasGroup: alpha={canvasGroup.alpha}, interactable={canvasGroup.interactable}");
        }
        
        // Check RectTransform
        RectTransform rect = overlay.GetComponent<RectTransform>();
        if (rect != null)
        {
            Debug.Log($"   - RectTransform: pos={rect.anchoredPosition}, size={rect.sizeDelta}, scale={rect.localScale}");
        }
    }

    public void HideOverlay(GameObject overlay)
    {
        if (overlay == null) return;

        overlay.SetActive(false);

        Stack<GameObject> tempStack = new Stack<GameObject>();
        while (overlayHistory.Count > 0)
        {
            var o = overlayHistory.Pop();
            if (o != overlay) tempStack.Push(o);
        }
        while (tempStack.Count > 0)
            overlayHistory.Push(tempStack.Pop());
    }

    public void HideAllOverlays()
    {
        while (overlayHistory.Count > 0)
        {
            var o = overlayHistory.Pop();
            if (o != null) o.SetActive(false);
        }
    }
    #endregion

    #region Content Popup Methods
    // ADDED: 3-parameter version for Learn More button support
    public void ShowTipPopup(string title, string content, string link = "")
    {
        Debug.Log("🎯🎯🎯 UIManager.ShowTipPopup (3 parameters) CALLED!");
        Debug.Log($"🎯 Title: {title}, Content length: {content?.Length ?? 0}, Link: '{link}'");
        
        // Ensure reference is set
        if (pageTipPopup == null)
        {
            EnsurePopupReference();
        }

        if (pageTipPopup != null)
        {
            Debug.Log($"✅ pageTipPopup found: {pageTipPopup.name}");
            Debug.Log($"✅ Popup active before: {pageTipPopup.activeInHierarchy}");
            
            // Comprehensive visibility check before showing
            Debug.Log("🔍 PRE-SHOW VISIBILITY CHECK:");
            CheckParentHierarchy(pageTipPopup.transform);
            CheckUIComponents(pageTipPopup);
            
            var popupScript = pageTipPopup.GetComponent<EyeTipPopup>();
            if (popupScript != null)
            {
                Debug.Log("✅ EyeTipPopup script found!");
                Debug.Log("✅ CALLING EyeTipPopup.ShowTip() with link parameter NOW!");
                popupScript.ShowTip(title, content, link);
                Debug.Log("✅ EyeTipPopup.ShowTip() called!");
                
                // Show overlay with enhanced visibility
                ShowOverlay(pageTipPopup);
                Debug.Log("✅ ShowOverlay called!");
                
                // Post-show verification
                Debug.Log("🔍 POST-SHOW VERIFICATION:");
                Debug.Log($"✅ Popup active after: {pageTipPopup.activeInHierarchy}");
                CheckParentHierarchy(pageTipPopup.transform);
                
                // Force one more activation to be sure
                pageTipPopup.SetActive(true);
                pageTipPopup.transform.SetAsLastSibling();
                Canvas.ForceUpdateCanvases();
                
                Debug.Log($"✅ Final active state: {pageTipPopup.activeInHierarchy}");
            }
            else
            {
                Debug.LogError("❌❌❌ EyeTipPopup script not found on pageTipPopup GameObject!");
                
                // Debug what components ARE on the GameObject
                var components = pageTipPopup.GetComponents<Component>();
                Debug.LogError("Components on pageTipPopup:");
                foreach (var comp in components)
                {
                    Debug.LogError($"  - {comp.GetType().Name}");
                }

                // Fallback: just show the popup without script functionality
                ShowOverlay(pageTipPopup);
            }
        }
        else
        {
            Debug.LogError("❌❌❌ pageTipPopup reference not set after search!");
        }
    }

    // KEEP: Original 2-parameter version for backward compatibility
    public void ShowTipPopup(string title, string content)
    {
        Debug.Log("🎯 UIManager.ShowTipPopup (2 parameters) CALLED!");
        // Call the 3-parameter version with empty link
        ShowTipPopup(title, content, "");
    }

    public void ShowIssuePopup(string title, string content, string link = "")
    {
        Debug.Log("🎯🎯🎯 UIManager.ShowIssuePopup CALLED!");
        
        // Ensure reference is set
        if (pageIssuePopup == null)
        {
            EnsureIssuePopupReference();
        }

        // If we have a dedicated issue popup, use it
        if (pageIssuePopup != null)
        {
            Debug.Log($"✅ pageIssuePopup found: {pageIssuePopup.name}");
            Debug.Log($"✅ Popup active before: {pageIssuePopup.activeInHierarchy}");
            
            // Comprehensive visibility check before showing
            Debug.Log("🔍 PRE-SHOW VISIBILITY CHECK:");
            CheckParentHierarchy(pageIssuePopup.transform);
            CheckUIComponents(pageIssuePopup);
            
            var issueScript = pageIssuePopup.GetComponent<EyeIssuePopup>();
            if (issueScript != null)
            {
                Debug.Log("✅ EyeIssuePopup script found!");
                Debug.Log("✅ CALLING EyeIssuePopup.ShowIssue() NOW!");
                issueScript.ShowIssue(title, content, link);
                Debug.Log("✅ EyeIssuePopup.ShowIssue() called!");
                
                // Show overlay with enhanced visibility
                ShowOverlay(pageIssuePopup);
                Debug.Log("✅ ShowOverlay called!");
                
                // Post-show verification
                Debug.Log("🔍 POST-SHOW VERIFICATION:");
                Debug.Log($"✅ Popup active after: {pageIssuePopup.activeInHierarchy}");
                CheckParentHierarchy(pageIssuePopup.transform);
                
                // Force one more activation to be sure
                pageIssuePopup.SetActive(true);
                pageIssuePopup.transform.SetAsLastSibling();
                Canvas.ForceUpdateCanvases();
                
                Debug.Log($"✅ Final active state: {pageIssuePopup.activeInHierarchy}");
            }
            else
            {
                Debug.LogError("❌❌❌ EyeIssuePopup script not found on pageIssuePopup GameObject!");
                
                // Fallback to using tip popup
                Debug.Log("🔄 Falling back to tip popup for issue display");
                ShowTipPopup(title, content, link);
            }
        }
        else
        {
            // Fallback to using tip popup if no dedicated issue popup exists
            Debug.Log("🔄 No dedicated issue popup found, using tip popup instead");
            ShowTipPopup(title, content, link);
        }
    }

    public void ShowTriviaPopup(string title, string content, string link = "")
    {
        Debug.Log("🎯🎯🎯 UIManager.ShowTriviaPopup CALLED!");
        
        // Ensure reference is set
        if (pageTriviaPopup != null)
        {
            Debug.Log($"✅ pageTriviaPopup found: {pageTriviaPopup.name}");
            Debug.Log($"✅ Popup active before: {pageTriviaPopup.activeInHierarchy}");
            
            // Comprehensive visibility check before showing
            Debug.Log("🔍 PRE-SHOW VISIBILITY CHECK:");
            CheckParentHierarchy(pageTriviaPopup.transform);
            CheckUIComponents(pageTriviaPopup);
            
            var triviaScript = pageTriviaPopup.GetComponent<EyeTriviaPopup>();
            if (triviaScript != null)
            {
                Debug.Log("✅ EyeTriviaPopup script found!");
                Debug.Log("✅ CALLING EyeTriviaPopup.ShowTrivia() NOW!");
                triviaScript.ShowTrivia(title, content, link);
                Debug.Log("✅ EyeTriviaPopup.ShowTrivia() called!");
                
                // Show overlay with enhanced visibility
                ShowOverlay(pageTriviaPopup);
                Debug.Log("✅ ShowOverlay called!");
                
                // Post-show verification
                Debug.Log("🔍 POST-SHOW VERIFICATION:");
                Debug.Log($"✅ Popup active after: {pageTriviaPopup.activeInHierarchy}");
                CheckParentHierarchy(pageTriviaPopup.transform);
                
                // Force one more activation to be sure
                pageTriviaPopup.SetActive(true);
                pageTriviaPopup.transform.SetAsLastSibling();
                Canvas.ForceUpdateCanvases();
                
                Debug.Log($"✅ Final active state: {pageTriviaPopup.activeInHierarchy}");
            }
            else
            {
                Debug.LogError("❌❌❌ EyeTriviaPopup script not found on pageTriviaPopup GameObject!");
                
                // Fallback to using tip popup
                Debug.Log("🔄 Falling back to tip popup for trivia display");
                ShowTipPopup(title, content, link);
            }
        }
        else
        {
            // Fallback to using tip popup if no dedicated trivia popup exists
            Debug.Log("🔄 No dedicated trivia popup found, using tip popup instead");
            ShowTipPopup(title, content, link);
        }
    }

    public void CloseTipPopup()
    {
        Debug.Log("🎯 UIManager.CloseTipPopup called");
        
        if (pageTipPopup != null)
        {
            Debug.Log($"✅ Hiding popup: {pageTipPopup.name}");
            HideOverlay(pageTipPopup);
        }
        else
        {
            Debug.LogError("❌ pageTipPopup is null in CloseTipPopup!");
        }
    }

    public void CloseIssuePopup()
    {
        Debug.Log("🎯 UIManager.CloseIssuePopup called");
        
        if (pageIssuePopup != null)
        {
            Debug.Log($"✅ Hiding issue popup: {pageIssuePopup.name}");
            HideOverlay(pageIssuePopup);
        }
        else
        {
            Debug.Log("ℹ️ pageIssuePopup is null in CloseIssuePopup, closing tip popup instead");
            CloseTipPopup();
        }
    }

    public void CloseTriviaPopup()
    {
        Debug.Log("🎯 UIManager.CloseTriviaPopup called");
        
        if (pageTriviaPopup != null)
        {
            Debug.Log($"✅ Hiding trivia popup: {pageTriviaPopup.name}");
            HideOverlay(pageTriviaPopup);
        }
        else
        {
            Debug.Log("ℹ️ pageTriviaPopup is null in CloseTriviaPopup, closing tip popup instead");
            CloseTipPopup();
        }
    }
    #endregion
    
    #region Debug Methods
    public void DebugCurrentState()
    {
        Debug.Log($"=== UIMANAGER DEBUG ====");
        Debug.Log($"Current Page: {currentPage?.name ?? "null"}");
        Debug.Log($"History Count: {history.Count}");
        Debug.Log($"Overlay Count: {overlayHistory.Count}");
        Debug.Log($"Total Pages: {allPages?.Length ?? 0}");
        
        // List all registered pages
        Debug.Log($"Registered Pages:");
        for (int i = 0; i < allPages.Length; i++)
        {
            if (allPages[i] != null)
            {
                Debug.Log($"  {i}. {allPages[i].name} (active: {allPages[i].activeSelf})");
            }
            else
            {
                Debug.Log($"  {i}. [NULL]");
            }
        }
        
        if (history.Count > 0)
        {
            Debug.Log("History Stack:");
            int i = 1;
            foreach (var page in history)
            {
                Debug.Log($"  {i}. {page?.name ?? "null"}");
                i++;
            }
        }
        Debug.Log($"======================");
    }
    
    // Helper method for other managers to find welcome page
    public GameObject FindPageByName(string name)
    {
        foreach (GameObject page in allPages)
        {
            if (page != null && page.name == name)
            {
                return page;
            }
        }
        
        return null;
    }
    
    // Helper method for other managers to find page containing name
    public GameObject FindPageContaining(string namePart)
    {
        foreach (GameObject page in allPages)
        {
            if (page != null && page.name.ToLower().Contains(namePart.ToLower()))
            {
                return page;
            }
        }
        
        return null;
    }
    
    // NEW: Emergency fix for login page being active
    public void ForceHideLoginPage()
    {
        if (loginPage != null && loginPage.activeSelf)
        {
            Debug.LogError($"[UIManager] 🔥 EMERGENCY: Force hiding login page!");
            loginPage.SetActive(false);
        }
    }
    #endregion
}