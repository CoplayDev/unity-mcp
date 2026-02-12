using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// Global scene coordinator for the AI Recommendation System learning experience.
/// Orchestrates the feedback loop and manages the experience phases.
/// </summary>
public class GameManager : MonoBehaviour
{
    // Singleton instance
    public static GameManager Instance { get; private set; }

    // Events for cross-manager communication
    public UnityEvent OnProfileUpdated = new UnityEvent();
    public UnityEvent OnCandidatesUpdated = new UnityEvent();
    public UnityEvent OnRankingUpdated = new UnityEvent();
    public UnityEvent OnFeedbackLoopTick = new UnityEvent();
    public UnityEvent<string> OnExperiencePhaseChanged = new UnityEvent<string>();
    public UnityEvent<int> OnObjectiveProgressChanged = new UnityEvent<int>();

    [Header("Manager References")]
    public ProfileManager profileManager;
    public CandidateManager candidateManager;
    public RankingManager rankingManager;
    public InteractionManager interactionManager;

    [Header("Experience State")]
    public int pollinationCount = 0;
    public int targetPollinationCount = 3;
    private string currentPhase = "Intro";
    private bool hasViewedBeehive = false;
    private int flowersApproached = 0;
    private bool hasAttemptedOutOfCircle = false;
    private bool hasTargetedInCircle = false;
    private int postSpawnObservations = 0;

    [Header("Shared State")]
    public Vector3 profilePosition = Vector3.zero;
    public List<GameObject> candidateFlowers = new List<GameObject>();
    public List<GameObject> rankedFlowers = new List<GameObject>();

    [Header("Feedback HUD")]
    public bool feedbackHudEnabled = true;
    public GameObject feedbackHudPanel;

    [Header("Timing")]
    public float pollinationConfirmSeconds = 0.2f;
    public float profileDriftStartDelay = 0.3f;
    public float profileDriftDuration = 2.0f;
    public float spawnFeedbackDelay = 2.5f;
    public float attributeTagDuration = 1.5f;

    // Phase tracking
    private readonly string[] phases = { "Intro", "Explore", "Trigger", "Observe Feedback Loop", "Summary" };
    private int currentPhaseIndex = 0;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // Bootstrap managers
        RegisterManagers();
        InitializeSharedState();
        StartExperiencePhase("Intro");
        
        // Initialize feedback HUD
        if (feedbackHudEnabled && feedbackHudPanel != null)
        {
            UpdateFeedbackHUD();
        }
    }

    void RegisterManagers()
    {
        // Auto-find managers if not assigned
        if (profileManager == null) profileManager = FindObjectOfType<ProfileManager>();
        if (candidateManager == null) candidateManager = FindObjectOfType<CandidateManager>();
        if (rankingManager == null) rankingManager = FindObjectOfType<RankingManager>();
        if (interactionManager == null) interactionManager = FindObjectOfType<InteractionManager>();

        Debug.Log("GameManager: Managers registered");
    }

    void InitializeSharedState()
    {
        // Find beehive and set initial profile position
        GameObject beehive = GameObject.Find("Beehive");
        if (beehive != null)
        {
            profilePosition = beehive.transform.position;
        }

        Debug.Log($"GameManager: Shared state initialized - Profile position: {profilePosition}");
    }

    public void StartExperiencePhase(string phaseName)
    {
        currentPhase = phaseName;
        currentPhaseIndex = System.Array.IndexOf(phases, phaseName);
        
        Debug.Log($"GameManager: Starting phase '{phaseName}'");
        OnExperiencePhaseChanged.Invoke(phaseName);
        
        ShowGuidedPrompt(phaseName);
    }

    public void UpdateProgress(int value)
    {
        pollinationCount = value;
        OnObjectiveProgressChanged.Invoke(value);
        
        if (feedbackHudEnabled)
        {
            UpdateFeedbackHUD();
        }
        
        CheckPhaseCompletion();
    }

    void ShowGuidedPrompt(string phaseName)
    {
        string prompt = "";
        
        switch (phaseName)
        {
            case "Intro":
                prompt = "This beehive is your PROFILE. The glowing circle shows which flowers are CANDIDATES.";
                break;
            case "Explore":
                prompt = "Try to pollinate a flower OUTSIDE the circle—notice it won't count.";
                break;
            case "Trigger":
                prompt = "Aim at a highlighted flower and press Pollinate to record your preference.";
                break;
            case "Observe Feedback Loop":
                prompt = "Watch the beehive drift. Which flowers bloom first now? Pollinate again to reinforce a pattern.";
                break;
            case "Summary":
                prompt = "Match: PROFILE → ?, CANDIDATES → ?, RANKING → ?";
                break;
        }
        
        Debug.Log($"PROMPT: {prompt}");
        // In a full implementation, this would show in UI
    }

    void CheckPhaseCompletion()
    {
        bool shouldAdvance = false;

        switch (currentPhase)
        {
            case "Intro":
                // "Player has viewed the beehive and approached at least 2 flowers."
                shouldAdvance = hasViewedBeehive && flowersApproached >= 2;
                break;
                
            case "Explore":
                // "Player attempts to pollinate an out-of-circle flower and then targets an in-circle flower."
                shouldAdvance = hasAttemptedOutOfCircle && hasTargetedInCircle;
                break;
                
            case "Trigger":
                // "One successful pollination is registered."
                shouldAdvance = pollinationCount >= 1;
                break;
                
            case "Observe Feedback Loop":
                // "Player completes 3 pollination cycles and observes at least one post-spawn change in the garden."
                shouldAdvance = pollinationCount >= targetPollinationCount && postSpawnObservations >= 1;
                break;
                
            case "Summary":
                // Final phase - no auto-advancement
                shouldAdvance = false;
                break;
        }

        if (shouldAdvance)
        {
            AdvanceToNextPhase();
        }
    }

    void AdvanceToNextPhase()
    {
        if (currentPhaseIndex < phases.Length - 1)
        {
            currentPhaseIndex++;
            StartExperiencePhase(phases[currentPhaseIndex]);
        }
    }

    public void OnPollinationTriggered(GameObject flower)
    {
        pollinationCount++;
        
        // Immediate feedback
        Debug.Log($"Pollination confirmed on {flower.name}");
        
        // Queue profile update
        Invoke(nameof(TriggerProfileUpdate), pollinationConfirmSeconds);
        
        UpdateProgress(pollinationCount);
    }

    void TriggerProfileUpdate()
    {
        OnProfileUpdated.Invoke();
        
        // After profile drift completes, trigger feedback loop
        Invoke(nameof(TriggerFeedbackLoop), profileDriftDuration);
    }

    void TriggerFeedbackLoop()
    {
        OnFeedbackLoopTick.Invoke();
        
        // After spawn delay, update observations
        Invoke(nameof(IncrementPostSpawnObservations), spawnFeedbackDelay);
    }

    void IncrementPostSpawnObservations()
    {
        postSpawnObservations++;
        CheckPhaseCompletion();
    }

    public void NotifyBeehiveViewed()
    {
        hasViewedBeehive = true;
        CheckPhaseCompletion();
    }

    public void NotifyFlowerApproached()
    {
        flowersApproached++;
        CheckPhaseCompletion();
    }

    public void NotifyOutOfCircleAttempt()
    {
        hasAttemptedOutOfCircle = true;
        CheckPhaseCompletion();
    }

    public void NotifyInCircleTargeted()
    {
        hasTargetedInCircle = true;
        CheckPhaseCompletion();
    }

    void UpdateFeedbackHUD()
    {
        // This would update UI elements showing:
        // - Current objective
        // - Progress (pollinationCount / targetPollinationCount)
        // - Profile state
        // - Candidate count
        // - Top ranked flowers
        
        Debug.Log($"HUD Update - Phase: {currentPhase}, Progress: {pollinationCount}/{targetPollinationCount}");
    }

    public void UpdateProfilePosition(Vector3 newPosition)
    {
        profilePosition = newPosition;
        OnProfileUpdated.Invoke();
    }

    public void UpdateCandidates(List<GameObject> candidates)
    {
        candidateFlowers = candidates;
        OnCandidatesUpdated.Invoke();
    }

    public void UpdateRanking(List<GameObject> ranked)
    {
        rankedFlowers = ranked;
        OnRankingUpdated.Invoke();
    }
}
