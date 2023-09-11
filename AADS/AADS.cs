﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
#region Script

#region DONT YOU DARE TOUCH THESE
const string Version = "95.12.0";
const string Date = "2023/07/13";
const string CompatVersion = "170.0.0";
#endregion

/*
 AADS - Automated Air Defense System
    based off of LAMP.

 AADS Overview:
    AADS seeks to secure airspace automatically with no user input.
    AADS does this by having 3 (main) types of grids at its disposal.
        Batteries: Contains missiles that can be launched to intercept targets BVR.
        Trackers: Can track targets BVR.
        Detectors: Detects targets with turret homing logic.
    These grids work together to create a large network of tracking and intercepting.
    There are a few other grids to take note of.
    
    Battery: "Proximity Defense Missiles" - Missile Launchers that can detect and intercept targets,
        close range quick response
    Tracker: "Multi-Tracking Arrays" - Multitarget Tracker, when given one target MTAs will 
        scan around the target, and the target itself, for any deployed missiles or bombs. 
        Or if multiple targets are being broadcasted, will allocate more LIDAR-Nodes and track multiple 
        targets at once.
    Tracker: "Precision Targeting Arrays" - Highpowered Tracker best suited for 
        tracking NuclearMissiles. Uses its LIDAR-Nodes to track craft with a higher raycast frequency.
        Most accuracy due to its data update time.
    Detector: "Early-Warning-Detector" - Detects all enemy crafts and analyzes them to see if they are 
        a missile, countermeasure, or plane. Only if the target is deemed a missile it will lock onto it.
        When broadcasting, will let the network know that what it is tracking is a missile.
        Best suited for detecting nuclearMissile or cruiseMissile strikes.
      
        

*/

#region Fields
double
    _timeSinceAutofire = 141,
    _maxRaycastRange = 5000,
    _maxTimeForLockBreak = 3,
    _timeSinceTurretLock = 0;

bool
    _usePreciseAiming = false,
    _fireEnabled = true,
    _retask = false,
    _stealth = true,
    _spiral = false,
    _topdown = false,
    _isSetup = false,
    _killGuidance = false,
    _hasKilled = false,
    _inGravity = false,
    _broadcastRangeOverride = false;

const string IgcTagIff = "IGC_IFF_PKT",
    IgcTagRegisterEnemy = "IGC_RGSTR_PKT",
    IgcTagParams = "IGC_MSL_PAR_MSG",
    IgcTagHoming = "IGC_MSL_HOM_MSG",
    IgcTagBeamRide = "IGC_MSL_OPT_MSG",
    IgcTagFire = "IGC_MSL_FIRE_MSG",
    IgcTagRemoteFireRequest = "IGC_MSL_REM_REQ",
    IgcTagRemoteFireResponse = "IGC_MSL_REM_RSP",
    IgcTagRemoteFireNotification = "IGC_MSL_REM_NTF",
    IgcTagRegister = "IGC_MSL_REG_MSG",
    UnicastTag = "UNICAST",
    IniSectionSoundBase = "LAMP - Sound Config - Lock ",
    IniSectionTextSurf = "LAMP - Text Surface Config",
    IniTextSurfTemplate = "Show on screen {0}",
    MissileNumberText = "Missile number",
    TargetLockedText = "Target Locked",
    TargetNotLockedText = "No Target",
    TargetTooCloseText = "Target Too Close",
    TargetSearchingText = "Searching",
    ActiveText = "Active";

ConfigSection[] _config;

ConfigSection 
    _generalSection = new ConfigSection("LAMP - General Config"),
    _colorSection = new ConfigSection("LAMP - Status Screen Colors"),
    _siloDoorSection = new ConfigSection("LAMP - Silo Door Config"),
    _timerConfig = new ConfigSection("LAMP - Fire Timer Config");

ConfigString
    _fireControlGroupName = new ConfigString("Fire control group name", "Fire Control"),
    _missileNameTag = new ConfigString("Missile group name tag", "Missile"),
    _referenceNameTag = new ConfigString("Optional reference block name tag", "Reference");

ConfigBool
    _autofire = new ConfigBool("Enable auto-fire", false),
    _autoFireRemote = new ConfigBool("Auto-fire remote missiles", false),
    _stealthySemiActiveAntenna = new ConfigBool("Use dynamic active antenna range", false);

ConfigDouble
    _autoFireInterval = new ConfigDouble("Auto-fire interval (s)", 1),
    _idleAntennaRange = new ConfigDouble("Antenna range - Idle (m)", 800),
    _activeAntennaRange = new ConfigDouble("Antenna range - Active (m)", 5000),
    _minRaycastRange = new ConfigDouble("Minimum allowed lock on range (m)", 50),
    _searchScanRandomSpread = new ConfigDouble("Randomized raycast scan spread (m)", 0);

ConfigNullable<int, ConfigInt>
    _autofireLimitPerTarget = new ConfigNullable<int, ConfigInt>(new ConfigInt("Auto-fire missile limit per target")),
    _siloDoorNumber = new ConfigNullable<int, ConfigInt>(new ConfigInt(MissileNumberText, comment: " This door will be opened when this specified missile is fired")),
    _timerMissileNumber = new ConfigNullable<int, ConfigInt>(new ConfigInt(MissileNumberText, comment: " This timer will be triggered when this specified missile is fired"));
ConfigEnum<GuidanceMode> _preferredGuidanceMode = new ConfigEnum<GuidanceMode>("Preferred guidance mode", GuidanceMode.Camera, " Accepted guidance modes are:\n   Camera, Turret, or BeamRiding");
ConfigEnum<FireOrder> _fireOrder = new ConfigEnum<FireOrder>("Fire order", FireOrder.LowestMissileNumber, " Accepted values are:\n   LowestMissileNumber, SmallestAngleToTarget,\n   or SmallestDistanceToTarget\n Missiles will be fired smallest to largest value");
ConfigEnum<TriggerState>
    _timerTriggerState = new ConfigEnum<TriggerState>("Trigger on state", TriggerState.None,
        " This timer will be triggered when the script enters one (or more) of\n" +
        " the following states:\n" +
        "   None, Idle, Searching, Targeting, AnyFire\n" +
        " The \"Targeting\" state is triggered when a homing lock is established\n" +
        " OR when beam ride mode is activated. To trigger on multiple states\n" +
        " simply separate each state with commas (Ex: Idle,Targeting).");

ConfigDeprecated<bool, ConfigBool>
    _compatTimerTriggerAnyMissile = new ConfigDeprecated<bool, ConfigBool>(
        new ConfigBool("Trigger on any fire", false));

ConfigDeprecated<TriggerState, ConfigEnum<TriggerState>>
    _compatTimerTriggerState = new ConfigDeprecated<TriggerState, ConfigEnum<TriggerState>>(
        new ConfigEnum<TriggerState>("Trigger on targeting state", TriggerState.None));


public ConfigColor
    TopBarColor = new ConfigColor("Title bar background color", new Color(25, 25, 25)),
    TitleTextColor = new ConfigColor("Title text color", new Color(150, 150, 150)),
    BackgroundColor = new ConfigColor("Background color", new Color(0, 0, 0)),
    TextColor = new ConfigColor("Primary text color", new Color(150, 150, 150)),
    SecondaryTextColor = new ConfigColor("Secondary text color", new Color(75, 75, 75)),
    StatusTextColor = new ConfigColor("Status text color", new Color(150, 150, 150)),
    StatusBarBackgroundColor = new ConfigColor("Status bar background color", new Color(25, 25, 25)),
    GuidanceSelectedColor = new ConfigColor("Selected guidance outline color", new Color(0, 50, 0)),
    GuidanceAllowedColor = new ConfigColor("Allowed guidance text color", new Color(150, 150, 150)),
    GuidanceDisallowedColor = new ConfigColor("Disallowed guidance text color", new Color(25, 25, 25)),
    LockStatusGoodColor = new ConfigColor("Lock status good color", new Color(0, 50, 0)),
    LockStatusBadColor = new ConfigColor("Lock status bad color", new Color(50, 0, 0)),
    FireDisabledColor = new ConfigColor("Fire disabled text color", new Color(75, 75, 0)),
    FireDisabledBackgroundColor = new ConfigColor("Fire disabled background color", new Color(10, 10, 10, 200));

const double
    RuntimeToRealTime = (1.0 / 60.0) / 0.0166666,
    UpdatesPerSecond = 10,
    UpdateTime = 1.0 / UpdatesPerSecond;

Color
    _defaultTextColor = new Color(150, 150, 150),
    _targetLockedColor = new Color(150, 150, 150),
    _targetNotLockedColor = new Color(100, 0, 0),
    _targetSearchingColor = new Color(100, 100, 0),
    _targetTooCloseColor = new Color(100, 100, 0);

string _statusText = "";
Color _statusColor;
float _lockStrength = 0f;

ArgumentParser _args = new ArgumentParser();   
public enum GuidanceMode : int { None = 0, BeamRiding = 1, Camera = 1 << 1, Turret = 1 << 2 };
public enum GridType : int { None = 0, Battery = 1, Tracker = 1 << 1, Detector = 1 << 2};
public enum BatteryType : int {  Battery = 0, ProximityDefense = 1 };
public enum TrackerType : int { Tracker = 0, MultiTrackArray = 1, PrecisionTargetingArray = 1<< 1 };
public enum DetectorType : int { Detector = 0, EarlyWarning = 1};

public struct TargetInformation { Vector3D targetPosition; Vector3D targetVelocity; double timeSinceLastLock; };
enum TargetingStatus { Idle, Searching, Targeting };
enum FireOrder { LowestMissileNumber, SmallestDistanceToTarget, SmallestAngleToTarget };
[Flags] enum TriggerState { None = 0, Idle = 1, Searching = 1 << 1, Targeting = 1 << 2, AnyFire = 1 << 3 };

TriggerState[] _triggerStateValues;

Dictionary<long, int> _autofiredMissiles = new Dictionary<long, int>();

GuidanceMode _designationMode = GuidanceMode.None;
GridType _gridDuty = GridType.None;
public GuidanceMode DesignationMode
{
    get
    {
        return _designationMode;
    }
    set
    {
        if (_designationMode != value && _allowedGuidanceModes.Contains(value))
        {
            _designationMode = value;
            _raycastHoming.ClearLock();
        }
    }
}

TargetingStatus _currentTargetingStatus = TargetingStatus.Idle;
TargetingStatus CurrentTargetingStatus
{
    get
    {
        return _currentTargetingStatus;
    }
    set
    {
        if (_currentTargetingStatus != value)
        {
            _currentTargetingStatus = value;
            OnNewTargetingStatus();
        }
    }
}

GuidanceMode _allowedGuidanceEnum = GuidanceMode.None;
IMyTerminalBlock _lastControlledReference = null;
List<GuidanceMode> _allowedGuidanceModes = new List<GuidanceMode>();
List<GridType> _allowedGridDuties = new List<GridType>();
List<IMySoundBlock> _soundBlocks = new List<IMySoundBlock>();
List<IMyCameraBlock> _cameraList = new List<IMyCameraBlock>();
List<IMyRadioAntenna> _broadcastList = new List<IMyRadioAntenna>();
List<IMyTextSurface> _textSurfaces = new List<IMyTextSurface>();
List<IMyShipController> _shipControllers = new List<IMyShipController>();
List<IMyMechanicalConnectionBlock> _mech = new List<IMyMechanicalConnectionBlock>();
List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
List<IMyTurretControlBlock> _turretControlBlocks = new List<IMyTurretControlBlock>();
List<IMyTimerBlock> _statusTimersAnyFire = new List<IMyTimerBlock>(),
                    _statusTimersIdle = new List<IMyTimerBlock>(),
                    _statusTimersSearch = new List<IMyTimerBlock>(),
                    _statusTimersTargeting = new List<IMyTimerBlock>();
Dictionary<TriggerState, List<IMyTimerBlock>> _statusTimerMap;
Dictionary<int, List<IMyDoor>> _siloDoorDict = new Dictionary<int, List<IMyDoor>>();
Dictionary<int, List<IMyTimerBlock>> _fireTimerDict = new Dictionary<int, List<IMyTimerBlock>>();
Dictionary<long, MyTuple<Vector3D, Vector3D, double>> NETWORK_CONTACTS = new Dictionary<long, MyTuple<Vector3D, Vector3D, double>>();
StringBuilder _setupStringbuilder = new StringBuilder();
RuntimeTracker _runtimeTracker;
RaycastHoming _raycastHoming;
MissileStatusScreenHandler _screenHandler;
SoundBlockManager _soundManager = new SoundBlockManager();
MyIni _ini = new MyIni();
Scheduler _scheduler;
ScheduledAction _scheduledSetup;
StringBuilder _echoBuilder = new StringBuilder();
CircularBuffer<Action> _screenUpdateBuffer;
IMyTerminalBlock _reference = null;

IMyUnicastListener _unicastListener;
IMyBroadcastListener _remoteFireNotificationListener;
IMyBroadcastListener _remoteTargetNotificationListener;

ImmutableArray<MyTuple<byte, long, Vector3D, double>>.Builder _messageBuilder = ImmutableArray.CreateBuilder<MyTuple<byte, long, Vector3D, double>>();
ImmutableArray<MyTuple<Vector3D, Vector3D, long>>.Builder _contactBuilder = ImmutableArray.CreateBuilder<MyTuple<Vector3D, Vector3D, long>>();
bool _clearSpriteCache = false;

SoundConfig
    _lockSearchSound = new SoundConfig(IniSectionSoundBase + "Search", "ArcSoundBlockAlert2", 0.5f, 1f, true),
    _lockGoodSound = new SoundConfig(IniSectionSoundBase + "Good", "ArcSoundBlockAlert2", 0.2f, 1f, true),
    _lockBadSound = new SoundConfig(IniSectionSoundBase + "Bad", "ArcSoundBlockAlert1", 0.15f, 1f, true),
    _lockLostSound = new SoundConfig(IniSectionSoundBase + "Lost/Abort", "ArcSoundBlockAlert1", 0.5f, 0f, false);

public class SoundConfig
{
    public ConfigString Name;
    public ConfigFloat Duration;
    public ConfigFloat Interval;
    public ConfigBool Loop;

    ConfigSection _soundConfig;

    public SoundConfig(string section, string name, float duration, float interval, bool loop)
    {
        _soundConfig = new ConfigSection(section);
        _soundConfig.AddValues(
            Name = new ConfigString("Sound name", name),
            Duration = new ConfigFloat("Duration (s)", duration),
            Interval = new ConfigFloat("Loop interval (s)", interval),
            Loop = new ConfigBool("Should loop", loop)
        );

    }

    public void UpdateFrom(MyIni ini)
    {
        _soundConfig.Update(ref ini);
    }
};

#endregion

#region Main Methods
Program()
{
    _compatTimerTriggerAnyMissile.Callback = (v) => {
        if (v)
        {
            _timerTriggerState.Value |= TriggerState.AnyFire;
        }
    };

    _compatTimerTriggerState.Callback = (v) => {
        _timerTriggerState.Value |= v;
    };
    
    _config = new ConfigSection[]
    {
        _generalSection,
        _colorSection,
    };
        
    _generalSection.AddValues(
        _fireControlGroupName,
        _missileNameTag,
        _referenceNameTag,
        _preferredGuidanceMode,
        _autofire,
        _autoFireInterval,
        _autoFireRemote,
        _autofireLimitPerTarget,
        _fireOrder,
        _idleAntennaRange,
        _activeAntennaRange,
        _stealthySemiActiveAntenna,
        _minRaycastRange,
        _searchScanRandomSpread);

    _colorSection.AddValues(
        TopBarColor,
        TitleTextColor,
        BackgroundColor,
        TextColor,
        SecondaryTextColor,
        StatusTextColor,
        StatusBarBackgroundColor,
        GuidanceSelectedColor,
        GuidanceAllowedColor,
        GuidanceDisallowedColor);

    _siloDoorSection.AddValue(_siloDoorNumber);

    _timerConfig.AddValues(
        _compatTimerTriggerAnyMissile,
        _compatTimerTriggerState,
        _timerMissileNumber,
        _timerTriggerState);

    _statusTimerMap = new Dictionary<TriggerState, List<IMyTimerBlock>>()
    {
        {TriggerState.Idle, _statusTimersIdle},
        {TriggerState.Searching, _statusTimersSearch},
        {TriggerState.Targeting, _statusTimersTargeting},
        {TriggerState.AnyFire, _statusTimersAnyFire},
    };

    _triggerStateValues = (TriggerState[])Enum.GetValues(typeof(TriggerState));

    _unicastListener = IGC.UnicastListener;
    _unicastListener.SetMessageCallback(UnicastTag);

    _remoteFireNotificationListener = IGC.RegisterBroadcastListener(IgcTagRemoteFireNotification);
    _remoteFireNotificationListener.SetMessageCallback(IgcTagRemoteFireNotification);

    _remoteTargetNotificationListener = IGC.RegisterBroadcastListener(IgcTagRegisterEnemy);
    //_remoteTargetNotificationListener.SetMessageCallback();

    _raycastHoming = new RaycastHoming(_maxRaycastRange, _maxTimeForLockBreak, _minRaycastRange, Me.CubeGrid.EntityId);
    _raycastHoming.AddEntityTypeToFilter(MyDetectedEntityType.FloatingObject, MyDetectedEntityType.Planet, MyDetectedEntityType.Asteroid);

    _screenHandler = new MissileStatusScreenHandler(this);

    _isSetup = GrabBlocks();
    GetLargestGridRadius();
    ParseStorage();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    _runtimeTracker = new RuntimeTracker(this, capacity: 5 * 60); // 5 second buffer

    float step = 1f / 9f;
    _screenUpdateBuffer = new CircularBuffer<Action>(10);
    _screenUpdateBuffer.Add(() => _screenHandler.ComputeScreenParams(DesignationMode, _allowedGuidanceEnum, _lockStrength, _statusText, _statusColor, _maxRaycastRange, _inGravity, _stealth, _spiral, _topdown, _usePreciseAiming, _autofire, _fireEnabled));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 0 * step, 1 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 1 * step, 2 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 2 * step, 3 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 3 * step, 4 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 4 * step, 5 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 5 * step, 6 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 6 * step, 7 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 7 * step, 8 * step, _clearSpriteCache));
    _screenUpdateBuffer.Add(() => _screenHandler.DrawScreens(_textSurfaces, 8 * step, 9 * step, _clearSpriteCache));

    _scheduledSetup = new ScheduledAction(Setup, 0.1);

    _scheduler = new Scheduler(this);
    _scheduler.AddScheduledAction(_scheduledSetup);
    _scheduler.AddScheduledAction(PrintDetailedInfo, 1);
    _scheduler.AddScheduledAction(HandleDisplays, 60);
    _scheduler.AddScheduledAction(BroadcastProcess, UpdatesPerSecond);
    _scheduler.AddScheduledAction(NetworkTargets, 6);
    _scheduler.AddScheduledAction(updateRadarContactTime,60); // Update every tick; IMPORTANT;
    _scheduler.AddScheduledAction(GetLargestGridRadius, 1.0 / 30.0);
    _scheduler.AddScheduledAction(() => AgeFiredPrograms(1), 1);

    OnNewTargetingStatus();
}

void Main(string arg, UpdateType updateType)
{
    try
    {
        _runtimeTracker.AddRuntime();

        if ((updateType & UpdateType.IGC) != 0)
        {
            IgcMessageHandling();
        }
        else if (!string.IsNullOrWhiteSpace(arg))
        {
            ParseArguments(arg);
        }

        double lastRuntime = RuntimeToRealTime * Math.Max(Runtime.TimeSinceLastRun.TotalSeconds, 0);
        _timeSinceTurretLock += lastRuntime;
        if (_autofire)
        {
            _timeSinceAutofire += lastRuntime;
        }

        _scheduler.Update();
        _soundManager.Update((float)lastRuntime);
        _runtimeTracker.AddInstructions();
    }
    catch (Exception e)
    {
        string scriptName = "WMI Missile Fire Control";
        BlueScreenOfDeath.Show(Me.GetSurface(0), scriptName, Version, e);
        foreach (IMyTextSurface surface in _textSurfaces)
        {
            BlueScreenOfDeath.Show(surface, scriptName, Version, e);
        }
        throw e;
    }
}

void PrintDetailedInfo()
{
    _echoBuilder.AppendLine($"LAMP | Launch A Missile Program\n(Version {Version} - {Date})");
    _echoBuilder.AppendLine($"\nFor use with WHAM v{CompatVersion} or later.\n");
    _echoBuilder.AppendLine($"Next refresh in {Math.Max(_scheduledSetup.RunInterval - _scheduledSetup.TimeSinceLastRun, 0):N0} seconds\n");
    _echoBuilder.AppendLine($"Last setup result: {(_isSetup ? "SUCCESS" : "FAIL")}\n{_setupStringbuilder}");
    _echoBuilder.AppendLine(_runtimeTracker.Write());
    Echo(_echoBuilder.ToString());
    _echoBuilder.Clear();
}

void Setup()
{
    _clearSpriteCache = !_clearSpriteCache;
    _isSetup = GrabBlocks();
}

void HandleDisplays()
{
    _screenUpdateBuffer.MoveNext().Invoke();
}

void OnNewTargetingStatus()
{
    List<IMyTimerBlock> timers;
    switch (CurrentTargetingStatus)
    {
        case TargetingStatus.Idle:
            timers = _statusTimersIdle;
            break;
        case TargetingStatus.Searching:
            timers = _statusTimersSearch;
            break;
        case TargetingStatus.Targeting:
            timers = _statusTimersTargeting;
            break;
        default:
            return;
    }
    foreach (var t in timers)
    {
        t.Trigger();
    }
}

void BroadcastProcess()
{
    if (!_isSetup)
    {
        return;
    }

    bool shouldBroadcast = false;
    _statusColor = _defaultTextColor;
    _lockStrength = 0f;

    if (_shipControllers.Count > 0)
    {
        _inGravity = !Vector3D.IsZero(_shipControllers[0].GetNaturalGravity());
    }

    switch (_gridDuty)
    {
        case GridType.Battery:
            break;
        case GridType.Detector:
            HandleTurretHoming(ref shouldBroadcast);
            break;
        case GridType.Tracker:
            HandleCameraHoming(ref shouldBroadcast);
            break;
    }
            /*
    switch (DesignationMode)
    {
        case GuidanceMode.BeamRiding:
            HandleOptical(ref shouldBroadcast);
            break;
        case GuidanceMode.Camera:
            HandleCameraHoming(ref shouldBroadcast);
            break;
        case GuidanceMode.Turret:
            HandleTurretHoming(ref shouldBroadcast);
            break;
    }
            */

    if (shouldBroadcast) //or if kill command
    {
        BroadcastTargetingData();
        BroadcastParameterMessage();
    }
    else if (_broadcastRangeOverride)
    {
        _scheduler.AddQueuedAction(() => ScaleAntennaRange(_activeAntennaRange), 0);
        _scheduler.AddQueuedAction(BroadcastParameterMessage, 1.0 / 6.0);
    }
}

void BroadcastTargetingData()
{
    long broadcastKey = GetBroadcastKey();
    switch (DesignationMode)
    {
        case GuidanceMode.BeamRiding:
            SendMissileBeamRideMessage(
                _frontVec,
                _leftVec,
                _upVec,
                _originPos,
                broadcastKey);
            break;
        case GuidanceMode.Camera:
            SendMissileHomingMessage(
                _raycastHoming.HitPosition,
                _raycastHoming.TargetPosition,
                _raycastHoming.TargetVelocity,
                _raycastHoming.PreciseModeOffset,
                Me.CubeGrid.WorldAABB.Center,
                _raycastHoming.TimeSinceLastLock,
                _raycastHoming.TargetId,
                broadcastKey);
            break;
        case GuidanceMode.Turret:
            SendMissileHomingMessage(
                _targetInfo.HitPosition.Value,
                _targetInfo.Position,
                _targetInfo.Velocity,
                Vector3D.Zero,
                Me.CubeGrid.WorldAABB.Center,
                _timeSinceTurretLock,
                _targetInfo.EntityId,
                broadcastKey);
            break;
    }
}

void BroadcastParameterMessage()
{
    long broadcastKey = GetBroadcastKey();
    bool killNow = (_killGuidance && !_hasKilled);

    SendMissileParameterMessage(
        killNow,
        _stealth,
        _spiral,
        _topdown,
        _usePreciseAiming,
        _retask,
        broadcastKey);

    if (_retask)
    {
        _retask = false;
    }

    if (killNow)
    {
        _hasKilled = true;
    }

    if (_broadcastRangeOverride)
    {
        _broadcastRangeOverride = false;
    }
}

#region Guidance Moding
void HandleOptical(ref bool shouldBroadcast)
{
    shouldBroadcast = true;
    OpticalGuidance();
    ScaleAntennaRange(_activeAntennaRange);
    StopAllSounds();

    // Status
    _statusText = ActiveText;
    CurrentTargetingStatus = TargetingStatus.Targeting;
}

void HandleCameraHoming(ref bool shouldBroadcast)
{
    _raycastHoming.Update(UpdateTime, _cameraList, _shipControllers, _reference);

    double antennaRange = _idleAntennaRange;
    if (_raycastHoming.Status == RaycastHoming.TargetingStatus.Locked)
    {
        shouldBroadcast = true;

        // Antenna range
        if (_stealthySemiActiveAntenna)
        {
            antennaRange = Vector3D.Distance(base.Me.CubeGrid.WorldAABB.Center, _raycastHoming.TargetPosition) - _raycastHoming.TargetSize;
        }
        else
        {
            antennaRange = _activeAntennaRange;
        }

        // Play sound
        if (!_raycastHoming.MissedLastScan)
        {
            PlayLockOnSound(_soundBlocks);
        }
        else if (_raycastHoming.MissedLastScan)
        {
            PlayScanMissedSound(_soundBlocks);
        }

        // Status
        _lockStrength = 1f - (float)((_raycastHoming.TimeSinceLastLock - _raycastHoming.AutoScanInterval) / _raycastHoming.MaxTimeForLockBreak);
        _lockStrength = MathHelper.Clamp(_lockStrength, 0f, 1f);

        _statusText = TargetLockedText;
        _statusColor = _targetLockedColor;
        CurrentTargetingStatus = TargetingStatus.Targeting;

        HandleAutofire(_raycastHoming.TargetId);
    }
    else
    {
        // Sound
        if (_raycastHoming.IsScanning)
        {
            PlayLockSearchSound(_soundBlocks);
        }
        else if (_raycastHoming.LockLost)
        {
            _raycastHoming.AcknowledgeLockLost();
            PlayFireAbortSound(_soundBlocks);
        }

        // Status
        if (_raycastHoming.Status == RaycastHoming.TargetingStatus.NotLocked)
        {
            if (!_raycastHoming.IsScanning)
            {
                _statusText = TargetNotLockedText;
                _statusColor = _targetNotLockedColor;
                CurrentTargetingStatus = TargetingStatus.Idle;
            }
            else
            {
                _statusText = TargetSearchingText;
                _statusColor = _targetSearchingColor;
                CurrentTargetingStatus = TargetingStatus.Searching;
            }
        }
        else if (_raycastHoming.Status == RaycastHoming.TargetingStatus.TooClose)
        {
            _statusText = TargetTooCloseText;
            _statusColor = _targetTooCloseColor;
            CurrentTargetingStatus = TargetingStatus.Searching;
        }
    }

    // Set antenna range
    if (!_broadcastRangeOverride)
    {
        ScaleAntennaRange(antennaRange);
    }
}

void HandleTurretHoming(ref bool shouldBroadcast)
{
    // TODO: Make turret guidance populate fields
    TurretGuidance(_turrets, _turretControlBlocks);

    double antennaRange = _stealthySemiActiveAntenna.Value ? 1.0 : _idleAntennaRange.Value;
    if (_turretLocked)
    {
        shouldBroadcast = true;

        // Sound
        PlayLockOnSound(_soundBlocks);

        // Antenna range
        if (_stealthySemiActiveAntenna)
        {
            antennaRange = Vector3D.Distance(Me.CubeGrid.WorldAABB.Center, _targetInfo.Position) - 10.0;
        }
        else
        {
            antennaRange = _activeAntennaRange;
        }

        // Status
        _lockStrength = 1f;
        _statusText = TargetLockedText;
        _statusColor = _targetLockedColor;
        CurrentTargetingStatus = TargetingStatus.Targeting;

        HandleAutofire(_targetInfo.EntityId);
    }
    else
    {
        // Status
        _statusText = TargetNotLockedText;
        _statusColor = _targetNotLockedColor;
        StopAllSounds();

        CurrentTargetingStatus = TargetingStatus.Idle;
    }

    // Set antenna range
    if (!_broadcastRangeOverride)
    {
        ScaleAntennaRange(antennaRange);
    }
}

void HandleAutofire(long targetId)
{
    if (_autofire && FiringAllowed && _timeSinceAutofire >= _autoFireInterval)
    {
        if (_autofireLimitPerTarget.HasValue && _autofireLimitPerTarget.Value > 0)
        {
            int firedCount;
            if (_autofiredMissiles.TryGetValue(targetId, out firedCount))
            {
                if (firedCount >= _autofireLimitPerTarget.Value)
                {
                    return;
                }
            }
            else
            {
                firedCount = 0;
            }
            firedCount += 1;
            _autofiredMissiles[targetId] = firedCount;
        }
        // If we are a battery and have the enemy FIRE!
        if (_gridDuty == GridType.Battery)
        {
            FireNextMissile(1);
        }
        else // Tell a battery on the network to fire.
        {
            // Battery will do it itself..
            //RequestRemoteMissileFire();
        }

        _timeSinceAutofire = 0;
    }
}
#endregion

#endregion
#region NETWORK
    void processRadarContact(Vector3D contactPosition, Vector3D contactVelocity, long contactId)
        {
            if (NETWORK_CONTACTS.ContainsKey(contactId))
            {
                MyTuple<Vector3D, Vector3D, double> localContactData = NETWORK_CONTACTS[contactId];
                localContactData.Item1 = contactPosition;
                localContactData.Item2 = contactVelocity;
                localContactData.Item3 = 0;
                NETWORK_CONTACTS[contactId] = localContactData;
            }
            else
            {
                // Create new radar contact
                MyTuple<Vector3D, Vector3D, double> localContactData;
                localContactData.Item1 = contactPosition;
                localContactData.Item2 = contactVelocity;
                localContactData.Item3 = 0;
                NETWORK_CONTACTS.Add(contactId, localContactData);
            }
        }
        /// <summary>
        /// Updates the TimeSinceLastUpdate in each RadarContact
        /// Localized. Resets to 0 every time IGC sends new data.
        /// </summary>
    void updateRadarContactTime()
        {
            foreach (var item in NETWORK_CONTACTS)
            {
                MyTuple<Vector3D, Vector3D, double> localContactData = NETWORK_CONTACTS[item.Key];
                localContactData.Item3 += UpdateTime; // (1/60)
                NETWORK_CONTACTS[item.Key]= localContactData;
            }
        }
    MyTuple<Vector3D,Vector3D,double> getRadarContactData(long gridId)
        {
            if (NETWORK_CONTACTS.ContainsKey(gridId))
                return NETWORK_CONTACTS[gridId];
            else
            { MyTuple<Vector3D, Vector3D, double> data;
                data.Item1 = new Vector3D();
                data.Item2 = new Vector3D();
                data.Item3 = 0.0;
                return data;
            }

        }
#endregion
#region IGC Message Handling
void IgcMessageHandling()
{
    while (_unicastListener.HasPendingMessage)
    {
        MyIGCMessage message = _unicastListener.AcceptMessage();
        object data = message.Data;
        if (message.Tag == IgcTagRemoteFireResponse)
        {
            if (data is MyTuple<Vector3D, long>)
            {
                var payload = (MyTuple<Vector3D, long>)data;
                var response = new RemoteFireResponse((Vector3)payload.Item1, payload.Item2);
                if (!_remoteFireResponses.Contains(response))
                {
                    _remoteFireResponses.Add(response);
                }
            }
        }
    }

    while (_remoteFireNotificationListener.HasPendingMessage)
    {
        var msg = _remoteFireNotificationListener.AcceptMessage();
        if (msg.Data is int)
        {
            var missileNumber = (int)(msg.Data);
            OpenSiloDoor(missileNumber);
            TriggerFireTimer(missileNumber);
        }
    }
    // Add enemyRegister listener to update list of enemies.
    while (_remoteTargetNotificationListener.HasPendingMessage)
    {
        MyIGCMessage msg = _remoteTargetNotificationListener.AcceptMessage();
        // Should send, enemyGridId, Position, Velocity.
        if (msg.Data is MyTuple<Vector3D,Vector3D,long>)
        {
            var payload = (MyTuple<Vector3D,Vector3D, long>)msg.Data;
            //myTuple = new MyTuple<byte, long, Vector3D, double>((byte)relation, targetId, targetPos, 0);
            // v3d, v3d, double, long
            processRadarContact(payload.Item1,payload.Item2,payload.Item3);
        }
       
    }
}
#endregion

#region Remote Fire
int _remoteFireRequests = 0;
bool _awaitingResponse = false;

struct RemoteFireResponse
{
    public Vector3D Position;
    public long EntityId;

    public RemoteFireResponse(Vector3 pos, long id)
    {
        Position = pos;
        EntityId = id;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is RemoteFireResponse))
        {
            return false;
        }

        return this.Equals((RemoteFireResponse)obj);
    }

    public bool Equals(RemoteFireResponse other)
    {
        return other.EntityId == this.EntityId;
    }

    public override int GetHashCode()
    {
        return this.EntityId.GetHashCode();
    }
}

void RequestRemoteMissileFire()
{
    _remoteFireRequests++;

    if (!_awaitingResponse)
    {
        var payload = new MyTuple<Vector3D, long>(Me.GetPosition(), Me.EntityId);
        IGC.SendBroadcastMessage(IgcTagRemoteFireRequest, payload);

        // Delay processing (Gives missiles 20 ticks to respond)
        _scheduler.AddScheduledAction(ParseRemoteFireResponses, 3, true);
        _awaitingResponse = true;
    }
}

List<RemoteFireResponse> _remoteFireResponses = new List<RemoteFireResponse>();
void ParseRemoteFireResponses()
{
    Vector3D referencePosition = Me.GetPosition();
    switch (DesignationMode)
    {
        case GuidanceMode.Turret:
            referencePosition = _targetInfo.Position;
            break;

        case GuidanceMode.Camera:
            referencePosition = _raycastHoming.TargetPosition;
            break;
    }

    _remoteFireResponses.Sort((x, y) =>
    {
        var num1 = Vector3D.DistanceSquared(x.Position, referencePosition);
        var num2 = Vector3D.DistanceSquared(y.Position, referencePosition);
        return num1.CompareTo(num2);
    });

    long broadcastKey = GetBroadcastKey();
    if (broadcastKey <= 0)
    {
        PlayFireAbortSound(_soundBlocks);
    }
    else
    {
        for (int i = 0; i < _remoteFireResponses.Count; ++i)
        {
            if (i + 1 > _remoteFireRequests)
            {
                break;
            }

            var response = _remoteFireResponses[i];

            IGC.SendUnicastMessage(response.EntityId, IgcTagRegister, broadcastKey);
            IGC.SendUnicastMessage(response.EntityId, IgcTagFire, "");
        }
    }

    _remoteFireResponses.Clear();
    _awaitingResponse = false;
    _remoteFireRequests = 0;
}
#endregion

#region Broadcast IFF
IMyCubeGrid _biggestGrid;
double _biggestGridRadius;

void GetLargestGridRadius()
{
    _biggestGridRadius = Me.CubeGrid.WorldVolume.Radius;
    _biggestGrid = Me.CubeGrid;
    GridTerminalSystem.GetBlocksOfType<IMyMechanicalConnectionBlock>(null, b => {
        var m = (IMyMechanicalConnectionBlock)b;
        double rad = m.CubeGrid.WorldVolume.Radius;
        IMyCubeGrid grid = m.CubeGrid;

        if (m.IsAttached)
        {
            double radT = m.TopGrid.WorldVolume.Radius;
            if (radT > rad)
            {
                rad = radT;
                grid = m.TopGrid;
            }
        }

        if (rad > _biggestGridRadius)
        {
            _biggestGridRadius = rad;
            _biggestGrid = grid;
        }

        return false;
    });
}

void NetworkTargets()
{
    bool hasTarget = (DesignationMode == GuidanceMode.Camera && _raycastHoming.Status == RaycastHoming.TargetingStatus.Locked)
        || (DesignationMode == GuidanceMode.Turret && _turretLocked);

    int capacity = hasTarget ? 2 : 1;
    _messageBuilder.Capacity = capacity;
    _contactBuilder.Capacity = 1; // When more data is added, increase capacity.
    // Broadcast own position
    TargetRelation myType = _biggestGrid.GridSizeEnum == MyCubeSize.Large ? TargetRelation.LargeGrid : TargetRelation.SmallGrid;
    var myTuple = new MyTuple<byte, long, Vector3D, double>((byte)(TargetRelation.Friendly | myType), _biggestGrid.EntityId, _biggestGrid.WorldVolume.Center, _biggestGridRadius * _biggestGridRadius);
    _messageBuilder.Add(myTuple);

    if (hasTarget)
    {
        MyRelationsBetweenPlayerAndBlock relationBetweenPlayerAndBlock;
        MyDetectedEntityType type;
        long targetId;
        Vector3D targetPos;
        Vector3D targetVel;
        if (_raycastHoming.Status == RaycastHoming.TargetingStatus.Locked)
        {
            // Camera Tracking, detector/tracker has lock.
            relationBetweenPlayerAndBlock = _raycastHoming.TargetRelation;
            targetId = _raycastHoming.TargetId;
            targetPos = _raycastHoming.TargetCenter;
            targetVel = _raycastHoming.TargetVelocity;
            type = _raycastHoming.TargetType;
        }
        else //(_turretLocked)
        {
           // Turret Tracking, detector/tracker has a turret lock.
           // Should switch to camera lock on same grid.
           relationBetweenPlayerAndBlock = _targetInfo.Relationship;
           targetId = _targetInfo.EntityId;
           targetPos = _targetInfo.Position;
           targetVel = _targetInfo.Velocity;
           type = _targetInfo.Type;
        }
        /*
        switch (DesignationMode)
        {
            case GuidanceMode.Camera:
                relationBetweenPlayerAndBlock = _raycastHoming.TargetRelation;
                targetId = _raycastHoming.TargetId;
                targetPos = _raycastHoming.TargetCenter;
                type = _raycastHoming.TargetType;
                break;

            default: // Turret
                relationBetweenPlayerAndBlock = _targetInfo.Relationship;
                targetId = _targetInfo.EntityId;
                targetPos = _targetInfo.Position;
                type = _targetInfo.Type;
                break;
        }
        */
        TargetRelation relation = TargetRelation.Locked;
        switch (relationBetweenPlayerAndBlock)
        {
            case MyRelationsBetweenPlayerAndBlock.Owner:
            case MyRelationsBetweenPlayerAndBlock.Friends:
            case MyRelationsBetweenPlayerAndBlock.FactionShare:
                relation |= TargetRelation.Friendly;
                break;

            case MyRelationsBetweenPlayerAndBlock.Enemies:
                relation |= TargetRelation.Enemy;
                break;

            // Neutral is assumed if not friendly or enemy
            default:
                relation |= TargetRelation.Neutral;
                break;
        }

        switch (type)
        {
            case MyDetectedEntityType.LargeGrid:
                relation |= TargetRelation.LargeGrid;
                break;
            case MyDetectedEntityType.SmallGrid:
                relation |= TargetRelation.SmallGrid;
                break;
        }

        myTuple = new MyTuple<byte, long, Vector3D, double>((byte)relation, targetId, targetPos, 0);
        var networkTuple = new MyTuple<Vector3D, Vector3D, long>(targetPos,targetVel,targetId);
        processRadarContact(targetPos, targetVel, targetId); // UpdateOwn Target data.
        _messageBuilder.Add(myTuple);
        _contactBuilder.Add(networkTuple); 
    }
            if (_gridDuty == GridType.Tracker || _gridDuty == GridType.Detector)
            {

                IGC.SendBroadcastMessage(IgcTagIff, _messageBuilder.MoveToImmutable());
                IGC.SendBroadcastMessage(IgcTagRegisterEnemy, _contactBuilder.MoveToImmutable());
            }
    //RequestRemoteMissileFire();
}
#endregion

#region Save and Argument Parsing
const string StorageKey = "LAMP";
void Save()
{
    _ini.Clear();
    if (_raycastHoming.Status != RaycastHoming.TargetingStatus.Locked)
    {
        Storage = "";
        return;
    }
    int i = 0;
    _ini.Set(StorageKey, $"{i++}", (int)_raycastHoming.Status);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.HitPosition.X);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.HitPosition.Y);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.HitPosition.Z);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.TargetVelocity.X);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.TargetVelocity.Y);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.TargetVelocity.Z);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.PreciseModeOffset.X);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.PreciseModeOffset.Y);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.PreciseModeOffset.Z);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.TimeSinceLastLock);
    _ini.Set(StorageKey, $"{i++}", _raycastHoming.TargetId);
    Storage = _ini.ToString();
}

void ParseStorage()
{
    _ini.Clear();
    _ini.TryParse(Storage);
    int i = 0;
    var tgtStatus = (RaycastHoming.TargetingStatus)_ini.Get(StorageKey, $"{i++}").ToInt32();
    if (tgtStatus != RaycastHoming.TargetingStatus.Locked)
    {
        return;
    }

    Vector3D pos, vel, offset;
    pos.X = _ini.Get(StorageKey, $"{i++}").ToDouble();
    pos.Y = _ini.Get(StorageKey, $"{i++}").ToDouble();
    pos.Z = _ini.Get(StorageKey, $"{i++}").ToDouble();
    vel.X = _ini.Get(StorageKey, $"{i++}").ToDouble();
    vel.Y = _ini.Get(StorageKey, $"{i++}").ToDouble();
    vel.Z = _ini.Get(StorageKey, $"{i++}").ToDouble();
    offset.X = _ini.Get(StorageKey, $"{i++}").ToDouble();
    offset.Y = _ini.Get(StorageKey, $"{i++}").ToDouble();
    offset.Z = _ini.Get(StorageKey, $"{i++}").ToDouble();
    double age = _ini.Get(StorageKey, $"{i++}").ToDouble();
    long id = _ini.Get(StorageKey, $"{i++}").ToInt64();

    DesignationMode = GuidanceMode.Camera;
    _raycastHoming.SetInitialLockParameters(pos, vel, offset, age, id);
}

bool FiringAllowed
{
    get
    {
        bool allowed = (DesignationMode == GuidanceMode.Camera && _raycastHoming.Status == RaycastHoming.TargetingStatus.Locked);
        allowed |= (DesignationMode == GuidanceMode.Turret && _turretLocked);
        allowed |= (DesignationMode == GuidanceMode.BeamRiding);
        allowed &= _fireEnabled;
        return allowed;
    }
}

void ParseArguments(string arg)
{
    if (!_args.TryParse(arg))
    {
        // TODO: Print error msg
        return;
    }

    switch (_args.Argument(0).ToLowerInvariant())
    {
        #region fire and kill commands
        case "enable_fire":
            _fireEnabled = true;
            break;

        case "disable_fire":
            _fireEnabled = false;
            break;

        case "fire":
            if (FiringAllowed)
            {
                int count = 1, start = 0, end = -1;
                bool useRange = false;

                if (_args.HasSwitch("range"))
                {
                    int rangeIdx = _args.GetSwitchIndex("range");
                    string startStr = _args.Argument(1 + rangeIdx);
                    string endStr = _args.Argument(2 + rangeIdx);
                    if (int.TryParse(startStr, out start) && int.TryParse(endStr, out end))
                    {
                        useRange = true;
                    }
                }

                if (_args.HasSwitch("count"))
                {
                    if (!int.TryParse(_args.Argument(1 + _args.GetSwitchIndex("count")), out count))
                    {
                        count = 1;
                    }
                }

                if (useRange)
                {
                    FireMissileInRange(count, start, end);
                }
                else
                {
                    FireNextMissile(count);
                }
            }
            else
            {
                PlayFireAbortSound(_soundBlocks);
            }
            break;

        case "remote_fire":
            if (FiringAllowed)
            {
                // No broadcast override needed since we have to be active to fire
                RequestRemoteMissileFire();
            }
            else
            {
                PlayFireAbortSound(_soundBlocks);
            }
            break;

        case "kill":
            _killGuidance = true;
            _hasKilled = false;
            _broadcastRangeOverride = true;
            break;

        case "alpha":
            if (FiringAllowed)
            {
                AlphaStrike();
            }
            else
            {
                PlayFireAbortSound(_soundBlocks);
            }
            break;
        #endregion

        #region stealth toggle
        case "stealth":
        case "stealth_switch":
            _stealth = !_stealth;
            _broadcastRangeOverride = true;
            break;

        case "stealth_on":
            _stealth = true;
            _broadcastRangeOverride = true;
            break;

        case "stealth_off":
            _stealth = false;
            _broadcastRangeOverride = true;
            break;
        #endregion

        #region spiral trajectory toggle
        case "evasion":
        case "evasion_switch":
        case "spiral":
        case "spiral_switch":
            _spiral = !_spiral;
            _broadcastRangeOverride = true;
            break;

        case "evasion_on":
        case "spiral_on":
            _spiral = true;
            _broadcastRangeOverride = true;
            break;

        case "evasion_off":
        case "spiral_off":
            _spiral = false;
            _broadcastRangeOverride = true;
            break;
        #endregion

        #region top down attack mode
        case "topdown":
        case "topdown_switch":
            _topdown = !_topdown;
            _broadcastRangeOverride = true;
            break;

        case "topdown_on":
            _topdown = true;
            _broadcastRangeOverride = true;
            break;

        case "topdown_off":
            _topdown = false;
            _broadcastRangeOverride = true;
            break;
        #endregion

        #region guidance switching
        case "mode_switch":
            CycleGuidanceModes();
            break;

        case "mode_beamride":
        case "mode_optical":
            DesignationMode = GuidanceMode.BeamRiding;
            break;

        case "mode_camera":
        case "mode_semiactive":
            DesignationMode = GuidanceMode.Camera;
            break;

        case "mode_turret":
            DesignationMode = GuidanceMode.Turret;
            break;
        #endregion

        #region lock on
        case "lock_on":
            if (_allowedGuidanceModes.Contains(GuidanceMode.Camera))
            {
                DesignationMode = GuidanceMode.Camera;
                _raycastHoming.LockOn();
            }
            break;

        case "lock_off":
            if (_allowedGuidanceModes.Contains(GuidanceMode.Camera))
            {
                DesignationMode = GuidanceMode.Camera;
                _raycastHoming.ClearLock();
                PlayFireAbortSound(_soundBlocks);
            }
            break;

        case "lock_switch":
            if (_allowedGuidanceModes.Contains(GuidanceMode.Camera))
            {
                DesignationMode = GuidanceMode.Camera;
                if (_raycastHoming.IsScanning)
                {
                    _raycastHoming.ClearLock();
                    PlayFireAbortSound(_soundBlocks);
                }
                else
                {
                    _raycastHoming.LockOn();
                }
            }
            break;

        case "retask":
            _retask = true;
            break;
        #endregion

        #region Precision mode
        case "precise":
        case "precise_switch":
            _usePreciseAiming = !_usePreciseAiming;
            _raycastHoming.OffsetTargeting = _usePreciseAiming;
            _broadcastRangeOverride = true;
            break;

        case "precise_on":
            _usePreciseAiming = true;
            _raycastHoming.OffsetTargeting = _usePreciseAiming;
            _broadcastRangeOverride = true;
            break;

        case "precise_off":
            _usePreciseAiming = false;
            _raycastHoming.OffsetTargeting = _usePreciseAiming;
            _broadcastRangeOverride = true;
            break;
        #endregion

        #region Auto fire toggle
        case "autofire":
        case "autofire_toggle":
        case "autofire_switch":
            _autofire.Value = !_autofire;
            break;

        case "autofire_on":
            _autofire.Value = true;
            break;

        case "autofire_off":
            _autofire.Value = false;
            break;
            #endregion
    }
}

void CycleGuidanceModes()
{
    if (_allowedGuidanceModes.Count == 0)
    {
        return;
    }

    int index = _allowedGuidanceModes.FindIndex(x => x == DesignationMode);
    index = ++index % _allowedGuidanceModes.Count;
    DesignationMode = _allowedGuidanceModes[index];
}
#endregion

#region Block Fetching

void ShallowClear<TKey, TValue>(Dictionary<TKey, List<TValue>> dict)
{
    foreach (List<TValue> list in dict.Values)
    {
        list.Clear();
    }
}

bool GrabBlocks()
{
    _setupStringbuilder.Clear();
    HandleIni();

    var group = GridTerminalSystem.GetBlockGroupWithName(_fireControlGroupName);
    if (group == null)
    {
        _setupStringbuilder.AppendLine($"> ERRROR: No block group named '{_fireControlGroupName}' was found");
        return false;
    }

    _soundBlocks.Clear();
    _cameraList.Clear();
    _broadcastList.Clear();
    _textSurfaces.Clear();
    _shipControllers.Clear();
    _mech.Clear();
    _turrets.Clear();
    _turretControlBlocks.Clear();
    _statusTimersAnyFire.Clear();
    _statusTimersIdle.Clear();
    _statusTimersSearch.Clear();
    _statusTimersTargeting.Clear();
    ShallowClear(_siloDoorDict);
    ShallowClear(_fireTimerDict);
    _reference = null;

    group.GetBlocksOfType<IMyTerminalBlock>(null, CollectionFunction);
    GridTerminalSystem.GetBlocksOfType(_shipControllers);
    GridTerminalSystem.GetBlocksOfType(_mech);

    _raycastHoming.ClearIgnoredGridIDs();
    _raycastHoming.AddIgnoredGridID(Me.CubeGrid.EntityId);
    foreach (var m in _mech)
    {
        _raycastHoming.AddIgnoredGridID(m.CubeGrid.EntityId);
        if (m.TopGrid != null)
        {
            _raycastHoming.AddIgnoredGridID(m.TopGrid.EntityId);
        }
    }

    _setupStringbuilder.AppendLine($"- Text surfaces: {_textSurfaces.Count}");
    _setupStringbuilder.AppendLine($"- Sound blocks: {_soundBlocks.Count}");

    _allowedGuidanceModes.Clear();

    // Camera guidance checks
    _setupStringbuilder.AppendLine($"- Cameras: {_cameraList.Count}");
    if (_cameraList.Count != 0)
    {
        _allowedGridDuties.Add(GridType.Tracker);
        _allowedGuidanceModes.Add(GuidanceMode.Camera);
    }

    // Turret guidance checks
    _setupStringbuilder.AppendLine($"- Turrets: {_turrets.Count}");
    _setupStringbuilder.AppendLine($"- Custom turret controllers: {_turretControlBlocks.Count}");
    if (_turrets.Count != 0 || _turretControlBlocks.Count != 0)
    {
        _allowedGridDuties.Add(GridType.Detector);
        _allowedGuidanceModes.Add(GuidanceMode.Turret);
    }

    // Optical guidance checks
    _setupStringbuilder.AppendLine($"- Ship controllers: {_shipControllers.Count}");
    _setupStringbuilder.AppendLine($"- Reference block: {(_reference != null ? $"'{_reference.CustomName}'" : "(none)")}");
    if (_shipControllers.Count != 0 || _cameraList.Count != 0 || _reference != null)
    {
        _allowedGuidanceModes.Add(GuidanceMode.BeamRiding);
    }

    //Antenna Blocks
    if (_broadcastList.Count == 0)
    {
        _setupStringbuilder.AppendLine($"> ERROR: No antennas");
        return false;
    }
    else
    {
        _setupStringbuilder.AppendLine($"- Antennas: {_broadcastList.Count}");
    }

    if (_allowedGuidanceModes.Count == 0)
    {
        // For AADS, not all grids will have Cameras or Turrets for guiding ( batteries )
        //_setupStringbuilder.AppendLine("> ERROR: No allowed guidance modes");
        //return false;
        _allowedGridDuties.Add(GridType.Battery);
    }

    if (DesignationMode == GuidanceMode.None)
    {
        if (_allowedGuidanceModes.Contains(_preferredGuidanceMode))
        {
            DesignationMode = _preferredGuidanceMode;
        }
    }

    if (DesignationMode == GuidanceMode.None)
    {
        DesignationMode = _allowedGuidanceModes[0];
    }

    
    if (_gridDuty == GridType.None)
    {
        if (_allowedGridDuties.Contains(GridType.Battery))
            _gridDuty = GridType.Battery;
        else if (_allowedGridDuties.Contains(GridType.Detector))
            _gridDuty = GridType.Detector;
        else if (_allowedGridDuties.Contains(GridType.Tracker))
            _gridDuty = GridType.Tracker;
    }

    _setupStringbuilder.AppendLine($"\nAllowed guidance modes:");
    _allowedGuidanceEnum = GuidanceMode.None;
    foreach (var mode in _allowedGuidanceModes)
    {
        _allowedGuidanceEnum |= mode;
        _setupStringbuilder.AppendLine($"- {mode}");
    }
    return true;
}

void HandleIni()
{
    _ini.Clear();
    bool parsed = _ini.TryParse(Me.CustomData);

    if (!parsed && !string.IsNullOrWhiteSpace(Me.CustomData))
    {
        _ini.Clear();
        _ini.EndContent = Me.CustomData;
    }

    foreach (var c in _config)
    {
        c.Update(ref _ini);
    }

    _lockSearchSound.UpdateFrom(_ini);
    _lockGoodSound.UpdateFrom(_ini);
    _lockBadSound.UpdateFrom(_ini);
    _lockLostSound.UpdateFrom(_ini);

    _raycastHoming.SearchScanSpread = _searchScanRandomSpread;

    string output = _ini.ToString();
    if (!string.Equals(output, Me.CustomData))
    {
        Me.CustomData = output;
    }
}

bool CollectionFunction(IMyTerminalBlock block)
{
    if (!block.IsSameConstructAs(Me))
    {
        return false;
    }

    AddTextSurfaces(block, _textSurfaces);

    if (block.CustomName.IndexOf(_referenceNameTag, StringComparison.OrdinalIgnoreCase) >= 0)
    {
        _reference = block;
    }

    // TODO: Only look for ship controllers in group? Maybe prioritize those in the group?

    var door = block as IMyDoor;
    if (door != null)
    {
        _ini.Clear();
        bool parsed = _ini.TryParse(door.CustomData);
        if (!parsed && !string.IsNullOrWhiteSpace(door.CustomData))
        {
            _ini.Clear();
            _ini.EndContent = door.CustomData;
        }

        _siloDoorSection.Update(ref _ini);

        if (_siloDoorNumber.HasValue)
        {
            List<IMyDoor> doors;
            if (!_siloDoorDict.TryGetValue(_siloDoorNumber.Value, out doors))
            {
                doors = new List<IMyDoor>();
                _siloDoorDict[_siloDoorNumber.Value] = doors;
            }
            doors.Add(door);
        }

        string output = _ini.ToString();
        if (!string.Equals(output, door.CustomData))
        {
            door.CustomData = output;
        }
        return false;
    }

    var timer = block as IMyTimerBlock;
    if (timer != null)
    {
        _ini.Clear();
        bool parsed = _ini.TryParse(timer.CustomData);

        if (!parsed && !string.IsNullOrWhiteSpace(timer.CustomData))
        {
            _ini.Clear();
            _ini.EndContent = timer.CustomData;
        }

        _timerTriggerState.Reset();
        
        _timerConfig.Update(ref _ini);

        if (_timerMissileNumber.HasValue)
        {
            List<IMyTimerBlock> timers;
            if (!_fireTimerDict.TryGetValue(_timerMissileNumber.Value, out timers))
            {
                timers = new List<IMyTimerBlock>();
                _fireTimerDict[_timerMissileNumber.Value] = timers;
            }
            timers.Add(timer);
        }

        foreach (TriggerState val in _triggerStateValues)
        {
            if (val == TriggerState.None)
            {
                continue;
            }

            if ((val & _timerTriggerState.Value) != 0)
            {
                List<IMyTimerBlock> timers;
                if (_statusTimerMap.TryGetValue(val, out timers))
                {
                    timers.Add(timer);
                }
            }
        }

        string output = _ini.ToString();
        if (!string.Equals(output, timer.CustomData))
        {
            timer.CustomData = output;
        }
        return false;
    }

    var soundBlock = block as IMySoundBlock;
    if (soundBlock != null)
    {
        _soundBlocks.Add(soundBlock);
        return false;
    }

    var camera = block as IMyCameraBlock;
    if (camera != null)
    {
        _cameraList.Add(camera);
        camera.EnableRaycast = true;
        return false;
    }

    var antenna = block as IMyRadioAntenna;
    if (antenna != null)
    {
        _broadcastList.Add(antenna);
        return false;
    }

    var turret = block as IMyLargeTurretBase;
    if (turret != null)
    {
        _turrets.Add(turret);
        return false;
    }

    var tcb = block as IMyTurretControlBlock;
    if (tcb != null)
    {
        _turretControlBlocks.Add(tcb);
        return false;
    }

    return false;
}

void AddTextSurfaces(IMyTerminalBlock block, List<IMyTextSurface> textSurfaces)
{
    var textSurface = block as IMyTextSurface;
    if (textSurface != null)
    {
        textSurfaces.Add(textSurface);
        return;
    }

    var surfaceProvider = block as IMyTextSurfaceProvider;
    if (surfaceProvider == null)
    {
        return;
    }

    _ini.Clear();
    bool parsed = _ini.TryParse(block.CustomData);
    if (!parsed && !string.IsNullOrWhiteSpace(block.CustomData))
    {
        _ini.Clear();
        _ini.EndContent = block.CustomData;
    }

    int surfaceCount = surfaceProvider.SurfaceCount;
    for (int i = 0; i < surfaceCount; ++i)
    {
        string iniKey = string.Format(IniTextSurfTemplate, i);
        bool display = _ini.Get(IniSectionTextSurf, iniKey).ToBoolean(i == 0 && !(block is IMyProgrammableBlock));
        if (display)
        {
            textSurfaces.Add(surfaceProvider.GetSurface(i));
        }

        _ini.Set(IniSectionTextSurf, iniKey, display);
    }

    string output = _ini.ToString();
    if (!string.Equals(output, block.CustomData))
    {
        block.CustomData = output;
    }
}
#endregion

#region Firing Methods
List<int> _currentMissileNumbers = new List<int>();
List<IMyProgrammableBlock> _missilePrograms = new List<IMyProgrammableBlock>();
Dictionary<int, IMyBlockGroup> _missileNumberDict = new Dictionary<int, IMyBlockGroup>();
Dictionary<IMyProgrammableBlock, double> _firedMissileProgramAge = new Dictionary<IMyProgrammableBlock, double>();
List<IMyProgrammableBlock> _firedMissilesKeyList = new List<IMyProgrammableBlock>();
List<IMyProgrammableBlock> _firedMissileProgramKeysToRemove = new List<IMyProgrammableBlock>();
const double MIN_PROGRAM_AGE_TO_REMOVE = 10.0;

void AgeFiredPrograms(double deltaTime)
{
    _firedMissilesKeyList.Clear();
    foreach (var key in _firedMissileProgramAge.Keys)
    {
        _firedMissilesKeyList.Add(key);
    }

    foreach (var key in _firedMissilesKeyList)
    {
        double elapsed = _firedMissileProgramAge[key];
        if (elapsed > MIN_PROGRAM_AGE_TO_REMOVE)
        {
            _firedMissileProgramKeysToRemove.Add(key);
        }
        else
        {
            _firedMissileProgramAge[key] = elapsed + deltaTime;
        }
    }

    foreach (var key in _firedMissileProgramKeysToRemove)
    {
        _firedMissileProgramAge.Remove(key);
    }
}

void GetCurrentMissiles()
{
    _currentMissileNumbers.Clear();
    _missileNumberDict.Clear();
    GridTerminalSystem.GetBlockGroups(null, CollectMissileNumbers);

    switch (_fireOrder.Value)
    {
        case FireOrder.LowestMissileNumber:
            _currentMissileNumbers.Sort();
            break;
        case FireOrder.SmallestAngleToTarget:
            _currentMissileNumbers.Sort((a, b) => MissileCompare(a, b, true));
            break;
        case FireOrder.SmallestDistanceToTarget:
            _currentMissileNumbers.Sort((a, b) => MissileCompare(a, b, false));
            break;
    }
}

int MissileCompare(int a, int b, bool angle)
{
    if (CurrentTargetingStatus != TargetingStatus.Targeting)
    {
        return -1;
    }
    return (int)Math.Sign(GetCompareValue(a, angle) - GetCompareValue(b, angle));
}

public static double CosBetween(Vector3D a, Vector3D b)
{
    if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
    {
        return 0;
    }
    else
    {
        return MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1);
    }
}


List<IMyShipController> _controllerCompareList = new List<IMyShipController>();
double GetCompareValue(int num, bool angle)
{
    _controllerCompareList.Clear();
    _missileNumberDict[num].GetBlocksOfType(_controllerCompareList);
    if (_controllerCompareList.Count == 0)
    {
        return double.MaxValue;
    }
    IMyShipController c = _controllerCompareList[0];

    Vector3D targetPos = Vector3D.Zero;
    switch (DesignationMode)
    {
        case GuidanceMode.BeamRiding:
            targetPos = _originPos + _frontVec * 200;
            break;
        case GuidanceMode.Camera:
            targetPos = _raycastHoming.TargetPosition;
            break;
        case GuidanceMode.Turret:
            targetPos = _targetInfo.Position;
            break;
    }

    if (angle)
    {
        return -CosBetween(c.WorldMatrix.Forward, targetPos - c.GetPosition());
    }
    else
    {
        return Vector3D.DistanceSquared(c.GetPosition(), targetPos);
    }
}

bool CollectMissileNumbers(IMyBlockGroup g)
{
    var number = GetMissileNumber(g, _missileNameTag);
    if (number < 0)
    {
        return false;
    }

    _currentMissileNumbers.Add(number);
    _missileNumberDict[number] = g;
    return false;
}

int GetMissileNumber(IMyBlockGroup group, string missileTag)
{
    string groupName = group.Name;
    // Check for tag
    if (!groupName.StartsWith(missileTag))
    {
        return -1;
    }

    // Check that string is long enough to have both a space and a number
    if (missileTag.Length + 2 > groupName.Length)
    {
        return -1;
    }

    // Check for space
    if (groupName[missileTag.Length] != ' ')
    {
        return -1;
    }

    // Check for number after space
    int startIdx = missileTag.Length + 1;
    int missileNumber;
    bool parsed = int.TryParse(groupName.Substring(startIdx), out missileNumber);
    if (!parsed)
    {
        return -1;
    }

    return missileNumber;
}

void AlphaStrike()
{
    GetCurrentMissiles();

    foreach (var missileNumber in _currentMissileNumbers)
    {
        FireMissilePrograms(missileNumber);
    }
}

bool IsMissilePBValid(IMyTerminalBlock b)
{
    var pb = (IMyProgrammableBlock)b;
    return pb.IsWorking && !_firedMissileProgramAge.ContainsKey(pb);
}

bool FireMissilePrograms(int missileNumber)
{
    IMyBlockGroup group = null;
    if (!_missileNumberDict.TryGetValue(missileNumber, out group))
    {
        return false;
    }

    _missilePrograms.Clear();
    group.GetBlocksOfType(_missilePrograms, IsMissilePBValid);
    if (_missilePrograms.Count == 0)
    {
        return false; // Could not fire
    }

    foreach (var pb in _missilePrograms)
    {
        IGC.SendUnicastMessage(pb.EntityId, IgcTagFire, "");
        _firedMissileProgramAge[pb] = 0;
    }

    OpenSiloDoor(missileNumber);
    TriggerFireTimer(missileNumber);

    BroadcastTargetingData();
    BroadcastParameterMessage();

    return true;
}

void FireMissileInRange(int numberToFire, int start, int end)
{
    GetCurrentMissiles();

    int numberFired = 0;
    foreach (var missileNumber in _currentMissileNumbers)
    {
        if (missileNumber < start || missileNumber > end)
        {
            continue;
        }

        bool fired = FireMissilePrograms(missileNumber);
        if (fired)
        {
            numberFired++;
        }

        if (numberFired >= numberToFire)
        {
            break;
        }
    }
}

void FireNextMissile(int numberToFire)
{
    GetCurrentMissiles();

    int numberFired = 0;
    foreach (var missileNumber in _currentMissileNumbers)
    {
        bool fired = FireMissilePrograms(missileNumber);
        if (fired)
        {
            numberFired++;
        }

        if (numberFired >= numberToFire)
        {
            break;
        }
    }
}

void OpenSiloDoor(int missileNumber)
{
    List<IMyDoor> doors;
    if (_siloDoorDict.TryGetValue(missileNumber, out doors))
    {
        foreach (IMyDoor d in doors)
        {
            d.Enabled = true;
            d.OpenDoor();
        }
    }
}

void TriggerFireTimer(int missileNumber)
{
    List<IMyTimerBlock> timers;
    if (_fireTimerDict.TryGetValue(missileNumber, out timers))
    {
        foreach (IMyTimerBlock t in timers)
        {
            t.Trigger();
        }
    }

    foreach (var t in _statusTimersAnyFire)
    {
        t.Trigger();
    }
}
#endregion

#region Optical Guidance
Vector3D _originPos = new Vector3D(0, 0, 0);
Vector3D _frontVec = new Vector3D(0, 0, 0);
Vector3D _leftVec = new Vector3D(0, 0, 0);
Vector3D _upVec = new Vector3D(0, 0, 0);

void OpticalGuidance()
{
    /*
        * The following prioritizes references in the following hierchy:
        * 1. Currently used camera
        * 2. Reference block (if any is specified)
        * 3. Currently used control seat
        * 4. Last active control seat
        * 5. First control seat that is found
        * 6. First camera that is found
        */

    IMyTerminalBlock reference = GetControlledCamera(_cameraList);

    if (reference == null)
    {
        reference = _reference;
    }

    if (reference == null)
    {
        reference = GetControlledShipController(_shipControllers);
    }

    if (reference == null)
    {
        if (_lastControlledReference != null)
        {
            reference = _lastControlledReference;
        }
        else if (_shipControllers.Count > 0)
        {
            reference = _shipControllers[0];
        }
        else if (_cameraList.Count > 0)
        {
            reference = _cameraList[0];
        }
        else
        {
            return;
        }
    }

    _lastControlledReference = reference;

    _originPos = reference.GetPosition();
    _frontVec = reference.WorldMatrix.Forward;
    _leftVec = reference.WorldMatrix.Left;
    _upVec = reference.WorldMatrix.Up;
}
#endregion

#region Turret Guidance
List<MyDetectedEntityInfo> _targetInfoList = new List<MyDetectedEntityInfo>();
MyDetectedEntityInfo _targetInfo = new MyDetectedEntityInfo();
double _maxTurretRange = 0;
bool _turretLocked = false;
void TurretGuidance(List<IMyLargeTurretBase> turrets, List<IMyTurretControlBlock> turretControlBlocks)
{
    //get targets
    _targetInfoList.Clear();
    _maxTurretRange = 0;
    foreach (var block in turrets)
    {
        if (block.HasTarget && !block.GetTargetedEntity().IsEmpty())
        {
            _targetInfoList.Add(block.GetTargetedEntity());
        }

        if (block.IsWorking && block.AIEnabled)
        {
            var thisRange = block.Range;
            if (thisRange > _maxTurretRange)
            {
                _maxTurretRange = thisRange;
            }
        }
    }

    foreach (var block in turretControlBlocks)
    {
        if (block.HasTarget && !block.GetTargetedEntity().IsEmpty())
        {
            _targetInfoList.Add(block.GetTargetedEntity());
        }

        if (block.IsWorking && block.AIEnabled)
        {
            var thisRange = block.Range;
            if (thisRange > _maxTurretRange)
            {
                _maxTurretRange = thisRange;
            }
        }
    }

    if (_targetInfoList.Count == 0)
    {
        if (_turretLocked)
        {
            PlayScanMissedSound(_soundBlocks);
        }
        _turretLocked = false;
        return;
    }

    _turretLocked = true;
    _timeSinceTurretLock = 0;

    //prioritize targets
    _targetInfoList.Sort((x, y) =>
    {
        var num1 = (x.Position - Me.GetPosition()).LengthSquared();
        var num2 = (y.Position - Me.GetPosition()).LengthSquared();
        return num1.CompareTo(num2);
    });

    //pick closest target
    _targetInfo = _targetInfoList[0];
    Vector3D targetVelocityVec = _targetInfo.Velocity;
}
#endregion

#region Sound Block Control

void StopAllSounds()
{
    _soundManager.ShouldPlay = false;
    _soundManager.ShouldLoop = false;
}

void PlayLockSearchSound(List<IMySoundBlock> soundBlocks)
{
    _soundManager.ShouldPlay = true;
    _soundManager.ShouldLoop = _lockSearchSound.Loop;
    _soundManager.SoundName = _lockSearchSound.Name;
    _soundManager.LoopDuration = _lockSearchSound.Interval;
    _soundManager.SoundDuration = _lockSearchSound.Duration;
    _soundManager.SoundBlocks = soundBlocks;
}

void PlayLockOnSound(List<IMySoundBlock> soundBlocks)
{
    _soundManager.ShouldPlay = true;
    _soundManager.ShouldLoop = _lockGoodSound.Loop;
    _soundManager.SoundName = _lockGoodSound.Name;
    _soundManager.LoopDuration = _lockGoodSound.Interval;
    _soundManager.SoundDuration = _lockGoodSound.Duration;
    _soundManager.SoundBlocks = soundBlocks;
}

void PlayFireAbortSound(List<IMySoundBlock> soundBlocks)
{
    _soundManager.ShouldPlay = false; // Force state change to cause the sound to be played immideately
    _soundManager.ShouldPlay = true;
    _soundManager.ShouldLoop = _lockLostSound.Loop;
    _soundManager.SoundName = _lockLostSound.Name;
    _soundManager.LoopDuration = _lockLostSound.Interval;
    _soundManager.SoundDuration = _lockLostSound.Duration;
    _soundManager.SoundBlocks = soundBlocks;
}

void PlayScanMissedSound(List<IMySoundBlock> soundBlocks)
{
    _soundManager.ShouldPlay = true;
    _soundManager.ShouldLoop = _lockBadSound.Loop;
    _soundManager.SoundName = _lockBadSound.Name;
    _soundManager.LoopDuration = _lockBadSound.Interval;
    _soundManager.SoundDuration = _lockBadSound.Duration;
    _soundManager.SoundBlocks = soundBlocks;
}

class SoundBlockManager
{
    public bool ShouldLoop = true;

    public float SoundDuration
    {
        get
        {
            return _soundDuration;
        }
        set
        {
            if (Math.Abs(value - _soundDuration) < 1e-3)
            {
                return;
            }
            _soundDuration = value;
            _settingsDirty = true;
        }
    }

    public float LoopDuration;

    public bool ShouldPlay
    {
        get
        {
            return _shouldPlay;
        }
        set
        {
            if (value == _shouldPlay)
            {
                return;
            }

            _shouldPlay = value;
            _hasPlayed = false;
        }
    }

    public string SoundName
    {
        get
        {
            return _soundName;
        }
        set
        {
            if (value == _soundName)
            {
                return;
            }
            _soundName = value;
            _settingsDirty = true;
        }
    }

    public List<IMySoundBlock> SoundBlocks;

    bool _settingsDirty = false;
    bool _shouldPlay = false;
    float _soundDuration;
    string _soundName;
    bool _hasPlayed = false;
    float _loopTime;
    float _soundPlayTime;

    enum SoundBlockAction { None = 0, UpdateSettings = 1, Play = 2, Stop = 4 }

    public void Update(float dt)
    {
        SoundBlockAction action = SoundBlockAction.None;

        if (_settingsDirty)
        {
            action |= SoundBlockAction.UpdateSettings;
            _settingsDirty = false;
        }

        if (ShouldPlay)
        {
            if (!_hasPlayed)
            {
                action |= SoundBlockAction.Play;
                _hasPlayed = true;
                _soundPlayTime = 0;
                _loopTime = 0;
            }
            else
            {
                _loopTime += dt;
                _soundPlayTime += dt;
                if (_soundPlayTime >= SoundDuration)
                {
                    action |= SoundBlockAction.Stop;
                    if (!ShouldLoop)
                    {
                        ShouldPlay = false;
                    }
                }

                if (ShouldLoop && _loopTime >= LoopDuration && _hasPlayed)
                {
                    _hasPlayed = false;
                }
            }
        }
        else
        {
            action |= SoundBlockAction.Stop;
        }

        // Apply sound block action
        if (action != SoundBlockAction.None && SoundBlocks != null)
        {
            foreach (var sb in SoundBlocks)
            {
                if ((action & SoundBlockAction.UpdateSettings) != 0)
                {
                    sb.LoopPeriod = 100f;
                    sb.SelectedSound = SoundName;
                }
                if ((action & SoundBlockAction.Play) != 0)
                {
                    sb.Play();
                }
                if ((action & SoundBlockAction.Stop) != 0)
                {
                    sb.Stop();
                }
            }
        }
    }
}

#endregion

#region Screen Display
/*
** Description:
**   Class for handling WHAM status screen displays.
**
** Dependencies:
**   MySpriteContainer
*/
public class MissileStatusScreenHandler
{
    #region Fields
    List<MySpriteContainer> _spriteContainers = new List<MySpriteContainer>();

    // Default sizes
    const float
        DefaultScreenHalfSize = 512 * 0.5f;

    // UI positions
    Vector2
        _topBarSize,
        _statusBarSize,
        _topBarPos,
        _topBarTextPos,
        _stealthTextPos,
        _aimPointTextPos,
        _rangeTextPos,
        _spiralTextPos,
        _topDownTextPos,
        _statusBarPos,
        _statusBarTextPos,
        _secondaryTextPosOffset,
        _dropShadowOffset,
        _modeCameraPos,
        _modeTurretPos,
        _modeBeamRidePos,
        _modeCameraSelectPos,
        _modeTurretSelectPos,
        _modeBeamRideSelectPos,
        _modeCameraSelectSize,
        _modeTurretSelectSize,
        _modeBeamRideSelectSize,
        _autofireTextPos,
        _fireDisabledPos,
        _fireDisabledTextBoxSize;

    // Constants
    const float
        PrimaryTextSize = 1.5f,
        SecondaryTextSize = 1.2f,
        BaseTextHeightPx = 37f, // 28.8
        PrimaryTextOffset = -0.5f * BaseTextHeightPx * PrimaryTextSize,
        ModeSelectLineLength = 20f,
        ModeSelectLineWidth = 6f;

    const string
        Font = "Debug",
        TopText = "LAMP Fire Control",
        ModeCameraText = "Camera",
        ModeTurretText = "Turret",
        ModeBeamRideText = "Beam Ride",
        RangeText = "Range",
        StealthText = "Stealth",
        SpiralText = "Evasion",
        TopdownText = "Topdown",
        EnabledText = "Enabled",
        DisabledText = "Disabled",
        NotApplicableText = "N/A",
        AimPointText = "Aim Point",
        AimCenterText = "Center",
        AimOffsetText = "Offset",
        AutofireText = "Autofire",
        FireDisabledText = "FIRING DISABLED";

    Program _p;
    #endregion

    public MissileStatusScreenHandler(Program program)
    {
        _p = program;

        _secondaryTextPosOffset = new Vector2(0, -1.5f * PrimaryTextOffset);
        _dropShadowOffset = new Vector2(2, 2);

        // Top bar
        _topBarSize = new Vector2(512, 64);
        _topBarPos = new Vector2(0, -DefaultScreenHalfSize + 32); //TODO: compute in ctor
        _topBarTextPos = new Vector2(0, -DefaultScreenHalfSize + 32 + PrimaryTextOffset);

        // Modes
        _modeCameraSelectSize = new Vector2(130, 56);
        _modeTurretSelectSize = new Vector2(110, 56);
        _modeBeamRideSelectSize = new Vector2(170, 56);

        _modeCameraSelectPos = new Vector2(-160, -140);
        _modeTurretSelectPos = new Vector2(-20, -140);
        _modeBeamRideSelectPos = new Vector2(140, -140);

        float secondaryTextVeticalOffset = -0.5f * BaseTextHeightPx * SecondaryTextSize;
        _modeCameraPos = _modeCameraSelectPos + new Vector2(0, secondaryTextVeticalOffset);
        _modeTurretPos = _modeTurretSelectPos + new Vector2(0, secondaryTextVeticalOffset);
        _modeBeamRidePos = _modeBeamRideSelectPos + new Vector2(0, secondaryTextVeticalOffset);

        // Status bar
        _statusBarPos = new Vector2(0, -70);
        _statusBarSize = new Vector2(450, 56);
        _statusBarTextPos = _statusBarPos + new Vector2(0, PrimaryTextOffset);

        // Left column
        _rangeTextPos = new Vector2(-220, 0 + PrimaryTextOffset);
        _spiralTextPos = new Vector2(-220, 90 + PrimaryTextOffset);
        _stealthTextPos = new Vector2(-220, 180 + PrimaryTextOffset);

        // Right column
        _aimPointTextPos = new Vector2(50, 0 + PrimaryTextOffset);
        _autofireTextPos = new Vector2(50, 90 + PrimaryTextOffset);
        _topDownTextPos = new Vector2(50, 180 + PrimaryTextOffset);

        // Fire disabled
        _fireDisabledPos = new Vector2(0, 50);
        _fireDisabledTextBoxSize = new Vector2(360, -PrimaryTextOffset * PrimaryTextSize + 24);
    }

    //For debugging
    public void Echo(string content)
    {
        _p.Echo(content);
    }

    public Color CustomInterpolation(Color color1, Color color2, float ratio)
    {
        Color midpoint = color1 + color2;
        if (ratio < 0.5)
        {
            return Color.Lerp(color1, midpoint, ratio * 2f);
        }
        return Color.Lerp(midpoint, color2, (ratio * 2f) - 1f);
    }

    public void ComputeScreenParams(GuidanceMode mode,
                                    GuidanceMode allowedModes,
                                    float lockStrength,
                                    string statusText,
                                    Color statusColor,
                                    double range,
                                    bool inGravity,
                                    bool stealth,
                                    bool spiral,
                                    bool topdown,
                                    bool precise,
                                    bool autofire,
                                    bool fireEnabled)
    {
        _spriteContainers.Clear();

        var guidanceMode = mode;
        var allowedModesEnum = allowedModes;
        bool showTopdownAndAimMode = true;
        bool anyGuidanceAllowed = allowedModesEnum != GuidanceMode.None;
        if (!anyGuidanceAllowed)
        {
            statusText = "ERROR";
            statusColor = _p.LockStatusBadColor;
        }

        Vector2 modeSelectSize = Vector2.Zero;
        Vector2 modeSelectPos = Vector2.Zero;

        if (guidanceMode == GuidanceMode.BeamRiding)
        {
            showTopdownAndAimMode = false;
            lockStrength = 1;

            modeSelectSize = _modeBeamRideSelectSize;
            modeSelectPos = _modeBeamRideSelectPos;
        }
        else
        {
            if (guidanceMode == GuidanceMode.Camera)
            {
                modeSelectSize = _modeCameraSelectSize;
                modeSelectPos = _modeCameraSelectPos;
            }
            else if (guidanceMode == GuidanceMode.Turret)
            {
                modeSelectSize = _modeTurretSelectSize;
                modeSelectPos = _modeTurretSelectPos;
            }
        }

        MySpriteContainer container;

        // Title bar
        container = new MySpriteContainer("SquareSimple", _topBarSize, _topBarPos, 0, _p.TopBarColor, true);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(TopText, Font, PrimaryTextSize, _topBarTextPos, _p.TitleTextColor);
        _spriteContainers.Add(container);

        // Status bar
        container = new MySpriteContainer("SquareSimple", _statusBarSize, _statusBarPos, 0, _p.StatusBarBackgroundColor);
        _spriteContainers.Add(container);

        Color lerpedStatusColor = CustomInterpolation(_p.LockStatusBadColor, _p.LockStatusGoodColor, lockStrength);
        Vector2 statusBarSize = _statusBarSize * new Vector2(lockStrength, 1f);
        container = new MySpriteContainer("SquareSimple", statusBarSize, _statusBarPos, 0, lerpedStatusColor);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(statusText, Font, PrimaryTextSize, _statusBarTextPos + _dropShadowOffset, Color.Black);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(statusText, Font, PrimaryTextSize, _statusBarTextPos, statusColor);
        _spriteContainers.Add(container);

        // Modes
        DrawBoxCorners(modeSelectSize, modeSelectPos, ModeSelectLineLength, ModeSelectLineWidth, _p.GuidanceSelectedColor, _spriteContainers);

        container = new MySpriteContainer(ModeCameraText, Font, SecondaryTextSize, _modeCameraPos, (allowedModesEnum & GuidanceMode.Camera) != 0 ? _p.GuidanceAllowedColor : _p.GuidanceDisallowedColor, TextAlignment.CENTER);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(ModeTurretText, Font, SecondaryTextSize, _modeTurretPos, (allowedModesEnum & GuidanceMode.Turret) != 0 ? _p.GuidanceAllowedColor : _p.GuidanceDisallowedColor, TextAlignment.CENTER);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(ModeBeamRideText, Font, SecondaryTextSize, _modeBeamRidePos, (allowedModesEnum & GuidanceMode.BeamRiding) != 0 ? _p.GuidanceAllowedColor : _p.GuidanceDisallowedColor, TextAlignment.CENTER);
        _spriteContainers.Add(container);

        // Range
        container = new MySpriteContainer(RangeText, Font, PrimaryTextSize, _rangeTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer($"{range * 0.001:n1} km", Font, SecondaryTextSize, _rangeTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Stealth
        container = new MySpriteContainer(StealthText, Font, PrimaryTextSize, _stealthTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(stealth ? EnabledText : DisabledText, Font, SecondaryTextSize, _stealthTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Spiral
        container = new MySpriteContainer(SpiralText, Font, PrimaryTextSize, _spiralTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(spiral ? EnabledText : DisabledText, Font, SecondaryTextSize, _spiralTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Topdown
        container = new MySpriteContainer(TopdownText, Font, PrimaryTextSize, _topDownTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer((!inGravity || !showTopdownAndAimMode) ? NotApplicableText : (topdown ? EnabledText : DisabledText), Font, SecondaryTextSize, _topDownTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Aimpoint
        container = new MySpriteContainer(AimPointText, Font, PrimaryTextSize, _aimPointTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(!showTopdownAndAimMode ? NotApplicableText : (precise ? AimOffsetText : AimCenterText), Font, SecondaryTextSize, _aimPointTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Autofire
        container = new MySpriteContainer(AutofireText, Font, PrimaryTextSize, _autofireTextPos, _p.TextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        container = new MySpriteContainer(!showTopdownAndAimMode ? NotApplicableText : (autofire ? EnabledText : DisabledText), Font, SecondaryTextSize, _autofireTextPos + _secondaryTextPosOffset, _p.SecondaryTextColor, TextAlignment.LEFT);
        _spriteContainers.Add(container);

        // Fire Disabled Warning
        if (!fireEnabled)
        {
            container = new MySpriteContainer("SquareSimple", _fireDisabledTextBoxSize, _fireDisabledPos, 0, _p.FireDisabledBackgroundColor);
            _spriteContainers.Add(container);

            container = new MySpriteContainer("AH_TextBox", _fireDisabledTextBoxSize, _fireDisabledPos, 0, _p.FireDisabledColor);
            _spriteContainers.Add(container);

            container = new MySpriteContainer(FireDisabledText, Font, PrimaryTextSize, _fireDisabledPos + new Vector2(0, -22), _p.FireDisabledColor, TextAlignment.CENTER);
            _spriteContainers.Add(container);
        }
    }

    public void DrawScreens(List<IMyTextSurface> surfaces, float startProportion, float endProportion, bool clearSpriteCache)
    {
        int startInt = (int)Math.Round(startProportion * surfaces.Count);
        int endInt = (int)Math.Round(endProportion * surfaces.Count);

        for (int i = startInt; i < endInt; ++i)
        {
            var surface = surfaces[i];

            surface.ContentType = ContentType.SCRIPT;
            surface.Script = "";
            surface.ScriptBackgroundColor = _p.BackgroundColor;


            Vector2 textureSize = surface.TextureSize;
            Vector2 screenCenter = textureSize * 0.5f;
            Vector2 viewportSize = surface.SurfaceSize;
            Vector2 scale = viewportSize / 512f;
            float minScale = Math.Min(scale.X, scale.Y);

            using (var frame = surface.DrawFrame())
            {
                if (clearSpriteCache)
                {
                    frame.Add(new MySprite());
                }

                foreach (var spriteContainer in _spriteContainers)
                {
                    frame.Add(spriteContainer.CreateSprite(minScale, ref screenCenter, ref viewportSize));
                }
            }
        }
    }

    /*
    Draws a box that looks like this:
        __        __
    |            |

    |__        __|
    */
    static void DrawBoxCorners(Vector2 boxSize, Vector2 centerPos, float lineLength, float lineWidth, Color color, List<MySpriteContainer> spriteContainers)
    {
        var horizontalSize = new Vector2(lineLength, lineWidth);
        var verticalSize = new Vector2(lineWidth, lineLength);

        Vector2 horizontalOffset = 0.5f * horizontalSize;
        Vector2 verticalOffset = 0.5f * verticalSize;

        Vector2 boxHalfSize = 0.5f * boxSize;
        Vector2 boxTopLeft = centerPos - boxHalfSize;
        Vector2 boxBottomRight = centerPos + boxHalfSize;
        Vector2 boxTopRight = centerPos + new Vector2(boxHalfSize.X, -boxHalfSize.Y);
        Vector2 boxBottomLeft = centerPos + new Vector2(-boxHalfSize.X, boxHalfSize.Y);

        MySpriteContainer container;

        // Top left
        container = new MySpriteContainer("SquareSimple", horizontalSize, boxTopLeft + horizontalOffset, 0, color);
        spriteContainers.Add(container);

        container = new MySpriteContainer("SquareSimple", verticalSize, boxTopLeft + verticalOffset, 0, color);
        spriteContainers.Add(container);

        // Top right
        container = new MySpriteContainer("SquareSimple", horizontalSize, boxTopRight + new Vector2(-horizontalOffset.X, horizontalOffset.Y), 0, color);
        spriteContainers.Add(container);

        container = new MySpriteContainer("SquareSimple", verticalSize, boxTopRight + new Vector2(-verticalOffset.X, verticalOffset.Y), 0, color);
        spriteContainers.Add(container);

        // Bottom left
        container = new MySpriteContainer("SquareSimple", horizontalSize, boxBottomLeft + new Vector2(horizontalOffset.X, -horizontalOffset.Y), 0, color);
        spriteContainers.Add(container);

        container = new MySpriteContainer("SquareSimple", verticalSize, boxBottomLeft + new Vector2(verticalOffset.X, -verticalOffset.Y), 0, color);
        spriteContainers.Add(container);

        // Bottom right
        container = new MySpriteContainer("SquareSimple", horizontalSize, boxBottomRight - horizontalOffset, 0, color);
        spriteContainers.Add(container);

        container = new MySpriteContainer("SquareSimple", verticalSize, boxBottomRight - verticalOffset, 0, color);
        spriteContainers.Add(container);
    }
}

#endregion

#region Antenna Broadcasting
void PopulateMatrix3x3Columns(ref Matrix3x3 mat, ref Vector3D col0, ref Vector3D col1, ref Vector3D col2)
{
    mat.M11 = (float)col0.X;
    mat.M21 = (float)col0.Y;
    mat.M31 = (float)col0.Z;

    mat.M12 = (float)col1.X;
    mat.M22 = (float)col1.Y;
    mat.M32 = (float)col1.Z;

    mat.M13 = (float)col2.X;
    mat.M23 = (float)col2.Y;
    mat.M33 = (float)col2.Z;
}


void SendMissileHomingMessage(Vector3D lastHitPosition, Vector3D targetPosition, Vector3D targetVelocity, Vector3D preciseOffset, Vector3D shooterPosition, double timeSinceLastLock, long targetId, long keycode)
{
    var matrix1 = new Matrix3x3();
    PopulateMatrix3x3Columns(ref matrix1, ref lastHitPosition, ref targetPosition, ref targetVelocity);

    var matrix2 = new Matrix3x3();
    PopulateMatrix3x3Columns(ref matrix2, ref preciseOffset, ref shooterPosition, ref Vector3D.Zero);

    var payload = new MyTuple<Matrix3x3, Matrix3x3, float, long, long>
    {
        Item1 = matrix1,
        Item2 = matrix2,
        Item3 = (float)timeSinceLastLock,
        Item4 = targetId,
        Item5 = keycode,
    };

    IGC.SendBroadcastMessage(IgcTagHoming, payload);
}

void SendMissileBeamRideMessage(Vector3D forward, Vector3D left, Vector3D up, Vector3D shooterPosition, long keycode)
{
    var payload = new MyTuple<Vector3, Vector3, Vector3, Vector3, long>
    {
        Item1 = (Vector3)forward,
        Item2 = (Vector3)left,
        Item3 = (Vector3)up,
        Item4 = (Vector3)shooterPosition,
        Item5 = keycode
    };

    IGC.SendBroadcastMessage(IgcTagBeamRide, payload);
}

void SendMissileParameterMessage(bool kill, bool stealth, bool spiral, bool topdown, bool precise, bool retask, long keycode)
{
    byte packedBools = 0;
    packedBools |= BoolToByte(kill);
    packedBools |= (byte)(BoolToByte(stealth) << 1);
    packedBools |= (byte)(BoolToByte(spiral) << 2);
    packedBools |= (byte)(BoolToByte(topdown) << 3);
    packedBools |= (byte)(BoolToByte(precise) << 4);
    packedBools |= (byte)(BoolToByte(retask) << 5);

    var payload = new MyTuple<byte, long>
    {
        Item1 = packedBools,
        Item2 = keycode
    };

    IGC.SendBroadcastMessage(IgcTagParams, payload);
}

byte BoolToByte(bool value)
{
    return value ? (byte)1 : (byte)0;
}

long GetBroadcastKey()
{
    long broadcastKey = -1;
    if (_broadcastList.Count > 0)
    {
        broadcastKey = _broadcastList[0].EntityId;
    }

    return broadcastKey;
}
#endregion

#region General Functions
IMyCameraBlock GetControlledCamera(List<IMyCameraBlock> cameras)
{
    foreach (var block in cameras)
    {
        if (block.IsActive)
        {
            return block;
        }
    }
    return null;
}

void ScaleAntennaRange(double dist)
{
    foreach (IMyRadioAntenna thisAntenna in _broadcastList)
    {
        thisAntenna.EnableBroadcasting = true;

        thisAntenna.Radius = (float)dist;
    }
}
#endregion

#endregion

#region INCLUDES

enum TargetRelation : byte { Neutral = 0, Other = 0, Enemy = 1, Friendly = 2, Locked = 4, LargeGrid = 8, SmallGrid = 16, Missile = 32, Asteroid = 64, RelationMask = Neutral | Enemy | Friendly, TypeMask = LargeGrid | SmallGrid | Other | Missile | Asteroid }

#region Raycast Homing
class RaycastHoming
{
    public TargetingStatus Status { get; private set; } = TargetingStatus.NotLocked;
    public Vector3D TargetPosition
    {
        get
        {
            return OffsetTargeting ? OffsetTargetPosition : TargetCenter;
        }
    }
    public double SearchScanSpread { get; set; } = 0;
    public Vector3D TargetCenter { get; private set; } = Vector3D.Zero;
    public Vector3D OffsetTargetPosition
    {
        get
        {
            return TargetCenter + Vector3D.TransformNormal(PreciseModeOffset, _targetOrientation);
        }
    }
    public Vector3D TargetVelocity { get; private set; } = Vector3D.Zero;
    public Vector3D HitPosition { get; private set; } = Vector3D.Zero;
    public Vector3D PreciseModeOffset { get; private set; } = Vector3D.Zero;
    public bool OffsetTargeting = false;
    public bool MissedLastScan { get; private set; } = false;
    public bool LockLost { get; private set; } = false;
    public bool IsScanning { get; private set; } = false;
    public double TimeSinceLastLock { get; private set; } = 0;
    public double TargetSize { get; private set; } = 0;
    public double MaxRange { get; private set; }
    public double MinRange { get; private set; }
    public long TargetId { get; private set; } = 0;
    public double AutoScanInterval { get; private set; } = 0;
    public double MaxTimeForLockBreak { get; private set; }
    public MyRelationsBetweenPlayerAndBlock TargetRelation { get; private set; }
    public MyDetectedEntityType TargetType { get; private set; }

    public enum TargetingStatus { NotLocked, Locked, TooClose };
    enum AimMode { Center, Offset, OffsetRelative };

    AimMode _currentAimMode = AimMode.Center;

    readonly HashSet<MyDetectedEntityType> _targetFilter = new HashSet<MyDetectedEntityType>();
    readonly List<IMyCameraBlock> _availableCameras = new List<IMyCameraBlock>();
    readonly Random _rngeesus = new Random();

    MatrixD _targetOrientation;
    HashSet<long> _gridIDsToIgnore = new HashSet<long>();
    double _timeSinceLastScan = 0;
    bool _manualLockOverride = false;
    bool _fudgeVectorSwitch = false;

    double AutoScanScaleFactor
    {
        get
        {
            return MissedLastScan ? 0.8 : 1.1;
        }
    }

    public RaycastHoming(double maxRange, double maxTimeForLockBreak, double minRange = 0, long gridIDToIgnore = 0)
    {
        MinRange = minRange;
        MaxRange = maxRange;
        MaxTimeForLockBreak = maxTimeForLockBreak;
        AddIgnoredGridID(gridIDToIgnore);
    }

    public void SetInitialLockParameters(Vector3D hitPosition, Vector3D targetVelocity, Vector3D offset, double timeSinceLastLock, long targetId)
    {
        TargetCenter = hitPosition;
        HitPosition = hitPosition;
        PreciseModeOffset = offset;
        TargetVelocity = targetVelocity;
        TimeSinceLastLock = timeSinceLastLock;
        _manualLockOverride = true;
        IsScanning = true;
        TargetId = targetId;
    }

    public void AddIgnoredGridID(long id)
    {
        _gridIDsToIgnore.Add(id);
    }

    public void ClearIgnoredGridIDs()
    {
        _gridIDsToIgnore.Clear();
    }

    public void AddEntityTypeToFilter(params MyDetectedEntityType[] types)
    {
        foreach (var type in types)
        {
            _targetFilter.Add(type);
        }
    }

    public void AcknowledgeLockLost()
    {
        LockLost = false;
    }

    public void LockOn()
    {
        ClearLockInternal();
        LockLost = false;
        IsScanning = true;
    }

    public void ClearLock()
    {
        ClearLockInternal();
        LockLost = false;
    }

    void ClearLockInternal()
    {
        IsScanning = false;
        Status = TargetingStatus.NotLocked;
        MissedLastScan = false;
        TimeSinceLastLock = 0;
        TargetSize = 0;
        HitPosition = Vector3D.Zero;
        TargetId = 0;
        _timeSinceLastScan = 141;
        _currentAimMode = AimMode.Center;
        TargetRelation = MyRelationsBetweenPlayerAndBlock.NoOwnership;
        TargetType = MyDetectedEntityType.None;
    }

    double RndDbl()
    {
        return 2 * _rngeesus.NextDouble() - 1;
    }

    double GaussRnd()
    {
        return (RndDbl() + RndDbl() + RndDbl()) / 3.0;
    }

    Vector3D CalculateFudgeVector(Vector3D targetDirection, double fudgeFactor = 5)
    {
        _fudgeVectorSwitch = !_fudgeVectorSwitch;

        if (!_fudgeVectorSwitch)
            return Vector3D.Zero;

        var perpVector1 = Vector3D.CalculatePerpendicularVector(targetDirection);
        var perpVector2 = Vector3D.Cross(perpVector1, targetDirection);
        if (!Vector3D.IsUnit(ref perpVector2))
            perpVector2.Normalize();

        var randomVector = GaussRnd() * perpVector1 + GaussRnd() * perpVector2;
        return randomVector * fudgeFactor * TimeSinceLastLock;
    }

    Vector3D GetSearchPos(Vector3D origin, Vector3D direction, IMyCameraBlock camera)
    {
        Vector3D scanPos = origin + direction * MaxRange;
        if (SearchScanSpread < 1e-2)
        {
            return scanPos;
        }
        return scanPos + (camera.WorldMatrix.Left * GaussRnd() + camera.WorldMatrix.Up * GaussRnd()) * SearchScanSpread;
    }

    IMyTerminalBlock GetReference(List<IMyCameraBlock> cameraList, List<IMyShipController> shipControllers, IMyTerminalBlock referenceBlock)
    {
        /*
         * References are prioritized in this order:
         * 1. Currently used camera
         * 2. Reference block
         * 3. Currently used control seat
         */
        IMyTerminalBlock controlledCam = GetControlledCamera(cameraList);
        if (controlledCam != null)
            return controlledCam;

        if (referenceBlock != null)
            return referenceBlock;

        return GetControlledShipController(shipControllers);
    }

    IMyCameraBlock SelectCamera()
    {
        // Check for transition between faces
        if (_availableCameras.Count == 0)
        {
            _timeSinceLastScan = 100000;
            MissedLastScan = true;
            return null;
        }

        return GetCameraWithMaxRange(_availableCameras);
    }

    void SetAutoScanInterval(double scanRange, IMyCameraBlock camera)
    {
        AutoScanInterval = scanRange / (1000.0 * camera.RaycastTimeMultiplier) / _availableCameras.Count * AutoScanScaleFactor;
    }

    bool DoLockScan(List<IMyCameraBlock> cameraList, out MyDetectedEntityInfo info, out IMyCameraBlock camera)
    {
        info = default(MyDetectedEntityInfo);

        #region Scan position selection
        Vector3D scanPosition;
        switch (_currentAimMode)
        {
            case AimMode.Offset:
                scanPosition = HitPosition;
                break;
            case AimMode.OffsetRelative:
                scanPosition = OffsetTargetPosition;
                break;
            default:
                scanPosition = TargetCenter;
                break;
        }
        scanPosition += TargetVelocity * TimeSinceLastLock;

        if (MissedLastScan)
        {
            scanPosition += CalculateFudgeVector(scanPosition - cameraList[0].GetPosition());
        }
        #endregion

        #region Camera selection
        GetCamerasInDirection(cameraList, _availableCameras, scanPosition, true);

        camera = SelectCamera();
        if (camera == null)
        {
            return false;
        }
        #endregion

        #region Scanning
        // We adjust the scan position to scan a bit past the target so we are more likely to hit if it is moving away
        Vector3D adjustedTargetPos = scanPosition + Vector3D.Normalize(scanPosition - camera.GetPosition()) * 2 * TargetSize;
        double scanRange = (adjustedTargetPos - camera.GetPosition()).Length();

        SetAutoScanInterval(scanRange, camera);

        if (camera.AvailableScanRange >= scanRange &&
            _timeSinceLastScan >= AutoScanInterval)
        {
            info = camera.Raycast(adjustedTargetPos);
            return true;
        }
        return false;
        #endregion
    }

    bool DoSearchScan(List<IMyCameraBlock> cameraList, IMyTerminalBlock reference, out MyDetectedEntityInfo info, out IMyCameraBlock camera)
    {
        info = default(MyDetectedEntityInfo);

        #region Camera selection
        if (reference != null)
        {
            GetCamerasInDirection(cameraList, _availableCameras, reference.WorldMatrix.Forward);
        }
        else
        {
            _availableCameras.Clear();
            _availableCameras.AddRange(cameraList);
        }

        camera = SelectCamera();
        if (camera == null)
        {
            return false;
        }
        #endregion

        #region Scanning
        SetAutoScanInterval(MaxRange, camera);

        if (camera.AvailableScanRange >= MaxRange &&
            _timeSinceLastScan >= AutoScanInterval)
        {
            if (reference != null)
            {
                info = camera.Raycast(GetSearchPos(reference.GetPosition(), reference.WorldMatrix.Forward, camera));
            }
            else
            {
                info = camera.Raycast(MaxRange);
            }

            return true;
        }
        return false;
        #endregion
    }

    public void UpdateTargetStateVectors(Vector3D position, Vector3D hitPosition, Vector3D velocity, double timeSinceLock = 0)
    {
        TargetCenter = position;
        HitPosition = hitPosition;
        TargetVelocity = velocity;
        TimeSinceLastLock = timeSinceLock;
    }

    void ProcessScanData(MyDetectedEntityInfo info, IMyTerminalBlock reference, Vector3D scanOrigin)
    {
        // Validate target and assign values
        if (info.IsEmpty() ||
            _targetFilter.Contains(info.Type) ||
            _gridIDsToIgnore.Contains(info.EntityId))
        {
            MissedLastScan = true;
            CycleAimMode();
        }
        else
        {
            if (Vector3D.DistanceSquared(info.Position, scanOrigin) < MinRange * MinRange && Status != TargetingStatus.Locked)
            {
                Status = TargetingStatus.TooClose;
                return;
            }

            if (info.EntityId != TargetId)
            {
                if (Status == TargetingStatus.Locked)
                {
                    MissedLastScan = true;
                    CycleAimMode();
                    return;
                }
                else if (_manualLockOverride)
                {
                    MissedLastScan = true;
                    return;
                }
            }

            MissedLastScan = false;
            UpdateTargetStateVectors(info.Position, info.HitPosition.Value, info.Velocity);
            TargetSize = info.BoundingBox.Size.Length();
            _targetOrientation = info.Orientation;

            if (Status != TargetingStatus.Locked) // Initial lockon
            {
                Status = TargetingStatus.Locked;
                TargetId = info.EntityId;
                TargetRelation = info.Relationship;
                TargetType = info.Type;

                // Compute aim offset
                if (!_manualLockOverride)
                {
                    Vector3D hitPosOffset = reference == null ? Vector3D.Zero : VectorRejection(reference.GetPosition() - scanOrigin, HitPosition - scanOrigin);
                    PreciseModeOffset = Vector3D.TransformNormal(info.HitPosition.Value + hitPosOffset - TargetCenter, MatrixD.Transpose(_targetOrientation));
                }
            }

            _manualLockOverride = false;
        }
    }

    void CycleAimMode()
    {
        _currentAimMode = (AimMode)((int)(_currentAimMode + 1) % 3);
    }

    public void Update(double timeStep, List<IMyCameraBlock> cameraList, List<IMyShipController> shipControllers, IMyTerminalBlock referenceBlock = null)
    {
        _timeSinceLastScan += timeStep;
        if (!IsScanning)
            return;

        TimeSinceLastLock += timeStep;

        if (cameraList.Count == 0)
            return;

        // Check for lock lost
        if (TimeSinceLastLock > (MaxTimeForLockBreak + AutoScanInterval) && (Status == TargetingStatus.Locked || _manualLockOverride))
        {
            LockLost = true; // TODO: Change this to a callback
            ClearLockInternal();
            return;
        }

        IMyTerminalBlock reference = GetReference(cameraList, shipControllers, referenceBlock);

        MyDetectedEntityInfo info;
        IMyCameraBlock camera;
        bool scanned;
        if (Status == TargetingStatus.Locked || _manualLockOverride)
        {
            scanned = DoLockScan(cameraList, out info, out camera);
        }
        else
        {
            scanned = DoSearchScan(cameraList, reference, out info, out camera);
        }

        if (!scanned)
        {
            return;
        }
        _timeSinceLastScan = 0;

        ProcessScanData(info, reference, camera.GetPosition());
    }

    void GetCamerasInDirection(List<IMyCameraBlock> allCameras, List<IMyCameraBlock> availableCameras, Vector3D testVector, bool vectorIsPosition = false)
    {
        availableCameras.Clear();

        foreach (var c in allCameras)
        {
            if (c.Closed)
                continue;

            if (TestCameraAngles(c, vectorIsPosition ? testVector - c.GetPosition() : testVector))
                availableCameras.Add(c);
        }
    }

    bool TestCameraAngles(IMyCameraBlock camera, Vector3D direction)
    {
        Vector3D local = Vector3D.Rotate(direction, MatrixD.Transpose(camera.WorldMatrix));

        if (local.Z > 0)
            return false;

        var yawTan = Math.Abs(local.X / local.Z);
        var localSq = local * local;
        var pitchTanSq = localSq.Y / (localSq.X + localSq.Z);

        return yawTan <= 1 && pitchTanSq <= 1;
    }

    IMyCameraBlock GetCameraWithMaxRange(List<IMyCameraBlock> cameras)
    {
        double maxRange = 0;
        IMyCameraBlock maxRangeCamera = null;
        foreach (var c in cameras)
        {
            if (c.AvailableScanRange > maxRange)
            {
                maxRangeCamera = c;
                maxRange = maxRangeCamera.AvailableScanRange;
            }
        }

        return maxRangeCamera;
    }

    IMyCameraBlock GetControlledCamera(List<IMyCameraBlock> cameras)
    {
        foreach (var c in cameras)
        {
            if (c.Closed)
                continue;

            if (c.IsActive)
                return c;
        }
        return null;
    }

    IMyShipController GetControlledShipController(List<IMyShipController> controllers)
    {
        if (controllers.Count == 0)
            return null;

        IMyShipController mainController = null;
        IMyShipController controlled = null;

        foreach (var sc in controllers)
        {
            if (sc.IsUnderControl && sc.CanControlShip)
            {
                if (controlled == null)
                {
                    controlled = sc;
                }

                if (sc.IsMainCockpit)
                {
                    mainController = sc; // Only one per grid so no null check needed
                }
            }
        }

        if (mainController != null)
            return mainController;

        if (controlled != null)
            return controlled;

        return controllers[0];
    }

    public static Vector3D VectorRejection(Vector3D a, Vector3D b)
    {
        if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
            return Vector3D.Zero;

        return a - a.Dot(b) / b.LengthSquared() * b;
    }
}
#endregion

/// <summary>
/// Class that tracks runtime history.
/// </summary>
public class RuntimeTracker
{
    public int Capacity { get; set; }
    public double Sensitivity { get; set; }
    public double MaxRuntime {get; private set;}
    public double MaxInstructions {get; private set;}
    public double AverageRuntime {get; private set;}
    public double AverageInstructions {get; private set;}
    public double LastRuntime {get; private set;}
    public double LastInstructions {get; private set;}
    
    readonly Queue<double> _runtimes = new Queue<double>();
    readonly Queue<double> _instructions = new Queue<double>();
    readonly int _instructionLimit;
    readonly Program _program;
    const double MS_PER_TICK = 16.6666;
    
    const string Format = "General Runtime Info\n"
            + "- Avg runtime: {0:n4} ms\n"
            + "- Last runtime: {1:n4} ms\n"
            + "- Max runtime: {2:n4} ms\n"
            + "- Avg instructions: {3:n2}\n"
            + "- Last instructions: {4:n0}\n"
            + "- Max instructions: {5:n0}\n"
            + "- Avg complexity: {6:0.000}%";

    public RuntimeTracker(Program program, int capacity = 100, double sensitivity = 0.005)
    {
        _program = program;
        Capacity = capacity;
        Sensitivity = sensitivity;
        _instructionLimit = _program.Runtime.MaxInstructionCount;
    }

    public void AddRuntime()
    {
        double runtime = _program.Runtime.LastRunTimeMs;
        LastRuntime = runtime;
        AverageRuntime += (Sensitivity * runtime);
        int roundedTicksSinceLastRuntime = (int)Math.Round(_program.Runtime.TimeSinceLastRun.TotalMilliseconds / MS_PER_TICK);
        if (roundedTicksSinceLastRuntime == 1)
        {
            AverageRuntime *= (1 - Sensitivity); 
        }
        else if (roundedTicksSinceLastRuntime > 1)
        {
            AverageRuntime *= Math.Pow((1 - Sensitivity), roundedTicksSinceLastRuntime);
        }

        _runtimes.Enqueue(runtime);
        if (_runtimes.Count == Capacity)
        {
            _runtimes.Dequeue();
        }
        
        MaxRuntime = _runtimes.Max();
    }

    public void AddInstructions()
    {
        double instructions = _program.Runtime.CurrentInstructionCount;
        LastInstructions = instructions;
        AverageInstructions = Sensitivity * (instructions - AverageInstructions) + AverageInstructions;
        
        _instructions.Enqueue(instructions);
        if (_instructions.Count == Capacity)
        {
            _instructions.Dequeue();
        }
        
        MaxInstructions = _instructions.Max();
    }

    public string Write()
    {
        return string.Format(
            Format,
            AverageRuntime,
            LastRuntime,
            MaxRuntime,
            AverageInstructions,
            LastInstructions,
            MaxInstructions,
            AverageInstructions / _instructionLimit);
    }
}

#region Scheduler
/// <summary>
/// Class for scheduling actions to occur at specific frequencies. Actions can be updated in parallel or in sequence (queued).
/// </summary>
public class Scheduler
{
    public double CurrentTimeSinceLastRun { get; private set; } = 0;
    public long CurrentTicksSinceLastRun { get; private set; } = 0;

    QueuedAction _currentlyQueuedAction = null;
    bool _firstRun = true;
    bool _inUpdate = false;

    readonly bool _ignoreFirstRun;
    readonly List<ScheduledAction> _actionsToAdd = new List<ScheduledAction>();
    readonly List<ScheduledAction> _scheduledActions = new List<ScheduledAction>();
    readonly List<ScheduledAction> _actionsToDispose = new List<ScheduledAction>();
    readonly Queue<QueuedAction> _queuedActions = new Queue<QueuedAction>();
    readonly Program _program;

    public const long TicksPerSecond = 60;
    public const double TickDurationSeconds = 1.0 / TicksPerSecond;
    const long ClockTicksPerGameTick = 166666L;

    /// <summary>
    /// Constructs a scheduler object with timing based on the runtime of the input program.
    /// </summary>
    public Scheduler(Program program, bool ignoreFirstRun = false)
    {
        _program = program;
        _ignoreFirstRun = ignoreFirstRun;
    }

    /// <summary>
    /// Updates all ScheduledAcions in the schedule and the queue.
    /// </summary>
    public void Update()
    {
        _inUpdate = true;
        long deltaTicks = Math.Max(0, _program.Runtime.TimeSinceLastRun.Ticks / ClockTicksPerGameTick);

        if (_firstRun)
        {
            if (_ignoreFirstRun)
            {
                deltaTicks = 0;
            }
            _firstRun = false;
        }

        _actionsToDispose.Clear();
        foreach (ScheduledAction action in _scheduledActions)
        {
            CurrentTicksSinceLastRun = action.TicksSinceLastRun + deltaTicks;
            CurrentTimeSinceLastRun = action.TimeSinceLastRun + deltaTicks * TickDurationSeconds;
            action.Update(deltaTicks);
            if (action.JustRan && action.DisposeAfterRun)
            {
                _actionsToDispose.Add(action);
            }
        }

        if (_actionsToDispose.Count > 0)
        {
            _scheduledActions.RemoveAll((x) => _actionsToDispose.Contains(x));
        }

        if (_currentlyQueuedAction == null)
        {
            // If queue is not empty, populate current queued action
            if (_queuedActions.Count != 0)
                _currentlyQueuedAction = _queuedActions.Dequeue();
        }

        // If queued action is populated
        if (_currentlyQueuedAction != null)
        {
            _currentlyQueuedAction.Update(deltaTicks);
            if (_currentlyQueuedAction.JustRan)
            {
                if (!_currentlyQueuedAction.DisposeAfterRun)
                {
                    _queuedActions.Enqueue(_currentlyQueuedAction);
                }
                // Set the queued action to null for the next cycle
                _currentlyQueuedAction = null;
            }
        }
        _inUpdate = false;

        if (_actionsToAdd.Count > 0)
        {
            _scheduledActions.AddRange(_actionsToAdd);
            _actionsToAdd.Clear();
        }
    }

    /// <summary>
    /// Adds an Action to the schedule. All actions are updated each update call.
    /// </summary>
    public void AddScheduledAction(Action action, double updateFrequency, bool disposeAfterRun = false, double timeOffset = 0)
    {
        ScheduledAction scheduledAction = new ScheduledAction(action, updateFrequency, disposeAfterRun, timeOffset);
        if (!_inUpdate)
            _scheduledActions.Add(scheduledAction);
        else
            _actionsToAdd.Add(scheduledAction);
    }

    /// <summary>
    /// Adds a ScheduledAction to the schedule. All actions are updated each update call.
    /// </summary>
    public void AddScheduledAction(ScheduledAction scheduledAction)
    {
        if (!_inUpdate)
            _scheduledActions.Add(scheduledAction);
        else
            _actionsToAdd.Add(scheduledAction);
    }

    /// <summary>
    /// Adds an Action to the queue. Queue is FIFO.
    /// </summary>
    public void AddQueuedAction(Action action, double updateInterval, bool removeAfterRun = false)
    {
        if (updateInterval <= 0)
        {
            updateInterval = 0.001; // avoids divide by zero
        }
        QueuedAction scheduledAction = new QueuedAction(action, updateInterval, removeAfterRun);
        _queuedActions.Enqueue(scheduledAction);
    }

    /// <summary>
    /// Adds a ScheduledAction to the queue. Queue is FIFO.
    /// </summary>
    public void AddQueuedAction(QueuedAction scheduledAction)
    {
        _queuedActions.Enqueue(scheduledAction);
    }
}

public class QueuedAction : ScheduledAction
{
    public QueuedAction(Action action, double runInterval, bool removeAfterRun = false)
        : base(action, 1.0 / runInterval, removeAfterRun: removeAfterRun, timeOffset: 0)
    { }
}

public class ScheduledAction
{
    public bool JustRan { get; private set; } = false;
    public bool DisposeAfterRun { get; private set; } = false;
    public double TimeSinceLastRun { get { return TicksSinceLastRun * Scheduler.TickDurationSeconds; } }
    public long TicksSinceLastRun { get; private set; } = 0;
    public double RunInterval
    {
        get
        {
            return RunIntervalTicks * Scheduler.TickDurationSeconds;
        }
        set
        {
            RunIntervalTicks = (long)Math.Round(value * Scheduler.TicksPerSecond);
        }
    }
    public long RunIntervalTicks
    {
        get
        {
            return _runIntervalTicks;
        }
        set
        {
            if (value == _runIntervalTicks)
                return;

            _runIntervalTicks = value < 0 ? 0 : value;
            _runFrequency = value == 0 ? double.MaxValue : Scheduler.TicksPerSecond / _runIntervalTicks;
        }
    }

    public double RunFrequency
    {
        get
        {
            return _runFrequency;
        }
        set
        {
            if (value == _runFrequency)
                return;

            if (value == 0)
                RunIntervalTicks = long.MaxValue;
            else
                RunIntervalTicks = (long)Math.Round(Scheduler.TicksPerSecond / value);
        }
    }

    long _runIntervalTicks;
    double _runFrequency;
    readonly Action _action;

    /// <summary>
    /// Class for scheduling an action to occur at a specified frequency (in Hz).
    /// </summary>
    /// <param name="action">Action to run</param>
    /// <param name="runFrequency">How often to run in Hz</param>
    public ScheduledAction(Action action, double runFrequency, bool removeAfterRun = false, double timeOffset = 0)
    {
        _action = action;
        RunFrequency = runFrequency; // Implicitly sets RunInterval
        DisposeAfterRun = removeAfterRun;
        TicksSinceLastRun = (long)Math.Round(timeOffset * Scheduler.TicksPerSecond);
    }

    public void Update(long deltaTicks)
    {
        TicksSinceLastRun += deltaTicks;

        if (TicksSinceLastRun >= RunIntervalTicks)
        {
            _action.Invoke();
            TicksSinceLastRun = 0;

            JustRan = true;
        }
        else
        {
            JustRan = false;
        }
    }
}
#endregion

/// <summary>
/// A simple, generic circular buffer class with a fixed capacity.
/// </summary>
/// <typeparam name="T"></typeparam>
public class CircularBuffer<T>
{
    public readonly int Capacity;

    T[] _array = null;
    int _setIndex = 0;
    int _getIndex = 0;

    /// <summary>
    /// CircularBuffer ctor.
    /// </summary>
    /// <param name="capacity">Capacity of the CircularBuffer.</param>
    public CircularBuffer(int capacity)
    {
        if (capacity < 1)
            throw new Exception($"Capacity of CircularBuffer ({capacity}) can not be less than 1");
        Capacity = capacity;
        _array = new T[Capacity];
    }

    /// <summary>
    /// Adds an item to the buffer. If the buffer is full, it will overwrite the oldest value.
    /// </summary>
    /// <param name="item"></param>
    public void Add(T item)
    {
        _array[_setIndex] = item;
        _setIndex = ++_setIndex % Capacity;
    }

    /// <summary>
    /// Retrieves the current item in the buffer and increments the buffer index.
    /// </summary>
    /// <returns></returns>
    public T MoveNext()
    {
        T val = _array[_getIndex];
        _getIndex = ++_getIndex % Capacity;
        return val;
    }

    /// <summary>
    /// Retrieves the current item in the buffer without incrementing the buffer index.
    /// </summary>
    /// <returns></returns>
    public T Peek()
    {
        return _array[_getIndex];
    }
}

#region Argument Parser
class ArgumentParser
{
    public int ArgumentCount {
        get;
        private set;
    } = 0;

    public string ErrorMessage
    {
        get;
        private set;
    }

    const char Quote = '"';
    List<string> _arguments = new List<string>();
    HashSet<string> _argHash = new HashSet<string>();
    HashSet<string> _switchHash = new HashSet<string>();
    Dictionary<string, int> _switchIndexDict = new Dictionary<string, int>();

    enum ReturnCode { EndOfStream = -1, Nominal = 0, NoArgs = 1, NonAlphaSwitch = 2, NoEndQuote = 3, NoSwitchName = 4 }

    string _raw;

    public bool InRange(int index)
    {
        if (index < 0 || index >= _arguments.Count)
        {
            return false;
        }
        return true;
    }

    public string Argument(int index)
    {
        if (!InRange(index))
        {
            return "";
        }

        return _arguments[index];
    }

    public bool IsSwitch(int index)
    {
        if (!InRange(index))
        {
            return false;
        }

        return _switchHash.Contains(_arguments[index]);
    }

    public int GetSwitchIndex(string switchName)
    {
        int idx;
        if (_switchIndexDict.TryGetValue(switchName, out idx))
        {
            return idx;
        }
        return -1;
    }

    ReturnCode GetArgStartIdx(int startIdx, out int idx, out bool isQuoted, out bool isSwitch)
    {
        idx = -1;
        isQuoted = false;
        isSwitch = false;
        for (int i = startIdx; i < _raw.Length; ++i)
        {
            char c = _raw[i];
            if (c != ' ')
            {
                if (c == Quote)
                {
                    isQuoted = true;
                    idx = i + 1;
                    return ReturnCode.Nominal;
                }
                if (c == '-' && i + 1 < _raw.Length && _raw[i+1] == '-')
                {
                    isSwitch = true;
                    idx = i + 2;
                    return ReturnCode.Nominal;
                }
                idx = i;
                return ReturnCode.Nominal;
            }
        }
        return ReturnCode.NoArgs;
    }

    ReturnCode GetArgLength(int startIdx, bool isQuoted, bool isSwitch, out int length)
    {
        length = 0;
        for (int i = startIdx; i < _raw.Length; ++i)
        {
            char c = _raw[i];
            if (isQuoted)
            {
                if (c == Quote)
                {
                    return ReturnCode.Nominal;
                }
            }
            else
            {
                if (c == ' ')
                {
                    if (isSwitch && length == 0)
                    {
                        return ReturnCode.NoSwitchName;
                    }
                    return ReturnCode.Nominal;
                }

                if (isSwitch)
                {
                    if (!char.IsLetter(c) && c != '_')
                    {
                        return ReturnCode.NonAlphaSwitch;
                    }
                } 
            }
            length++;
        }
        if (isQuoted)
        {
            return ReturnCode.NoEndQuote;
        }
        if (length == 0 && isSwitch)
        {
            return ReturnCode.NoSwitchName;
        }
        return ReturnCode.EndOfStream; // Reached end of stream
    }

    void ClearArguments()
    {
        ArgumentCount = 0;
        _arguments.Clear();
        _switchHash.Clear();
        _argHash.Clear();
        _switchIndexDict.Clear();
    }

    public bool HasArgument(string argName)
    {
        return _argHash.Contains(argName);
    }

    public bool HasSwitch(string switchName)
    {
        return _switchHash.Contains(switchName);
    }

    public bool TryParse(string arg)
    {
        ReturnCode status;

        _raw = arg;
        ClearArguments();

        int idx = 0;
        while (idx < _raw.Length)
        {
            bool isQuoted, isSwitch;
            int startIdx, length;
            string argString;
            status = GetArgStartIdx(idx, out startIdx, out isQuoted, out isSwitch);
            if (status == ReturnCode.NoArgs)
            {
                ErrorMessage = "";
                return true;
            }

            status = GetArgLength(startIdx, isQuoted, isSwitch, out length);
            if (status == ReturnCode.NoEndQuote)
            {
                ErrorMessage = $"No closing quote found! (idx: {startIdx})";
                ClearArguments();
                return false;
            }
            else if (status == ReturnCode.NonAlphaSwitch)
            {
                ErrorMessage = $"Switch can not contain non-alphabet characters! (idx: {startIdx})";
                ClearArguments();
                return false;
            }
            else if (status == ReturnCode.NoSwitchName)
            {
                ErrorMessage = $"Switch does not have a name (idx: {startIdx})";
                ClearArguments();
                return false;
            }
            else if (status == ReturnCode.EndOfStream) // End of stream
            {
                argString = _raw.Substring(startIdx);
                _arguments.Add(argString);
                _argHash.Add(argString);
                if (isSwitch)
                {
                    _switchHash.Add(argString);
                    _switchIndexDict[argString] = ArgumentCount;
                }
                ArgumentCount++;
                ErrorMessage = "";
                return true;
            }

            argString = _raw.Substring(startIdx, length);
            _arguments.Add(argString);
            _argHash.Add(argString);
            if (isSwitch)
            {
                _switchHash.Add(argString);
                _switchIndexDict[argString] = ArgumentCount;
            }
            ArgumentCount++;
            idx = startIdx + length;
            if (isQuoted)
            {
                idx++; // Move past the quote
            }
        }
        ErrorMessage = "";
        return true;
    }
}
#endregion

static class BlueScreenOfDeath 
{
    const int MAX_BSOD_WIDTH = 50;
    const string BSOD_TEMPLATE =
    "{0} - v{1}\n\n"+ 
    "A fatal exception has occured at\n"+
    "{2}. The current\n"+
    "program will be terminated.\n"+
    "\n"+ 
    "EXCEPTION:\n"+
    "{3}\n"+
    "\n"+
    "* Please REPORT this crash message to\n"+ 
    "  the Bug Reports discussion of this script\n"+ 
    "\n"+
    "* Press RECOMPILE to restart the program";

    static StringBuilder bsodBuilder = new StringBuilder(256);
    
    public static void Show(IMyTextSurface surface, string scriptName, string version, Exception e)
    {
        if (surface == null) 
        { 
            return;
        }
        surface.ContentType = ContentType.TEXT_AND_IMAGE;
        surface.Alignment = TextAlignment.LEFT;
        float scaleFactor = 512f / (float)Math.Min(surface.TextureSize.X, surface.TextureSize.Y);
        surface.FontSize = scaleFactor * surface.TextureSize.X / (19.5f * MAX_BSOD_WIDTH);
        surface.FontColor = Color.White;
        surface.BackgroundColor = Color.Blue;
        surface.Font = "Monospace";
        string exceptionStr = e.ToString();
        string[] exceptionLines = exceptionStr.Split('\n');
        bsodBuilder.Clear();
        foreach (string line in exceptionLines)
        {
            if (line.Length <= MAX_BSOD_WIDTH)
            {
                bsodBuilder.Append(line).Append("\n");
            }
            else
            {
                string[] words = line.Split(' ');
                int lineLength = 0;
                foreach (string word in words)
                {
                    lineLength += word.Length;
                    if (lineLength >= MAX_BSOD_WIDTH)
                    {
                        bsodBuilder.Append("\n");
                        lineLength = word.Length;
                    }
                    bsodBuilder.Append(word).Append(" ");
                    lineLength += 1;
                }
                bsodBuilder.Append("\n");
            }
        }

        surface.WriteText(string.Format(BSOD_TEMPLATE, 
                                        scriptName.ToUpperInvariant(),
                                        version,
                                        DateTime.Now, 
                                        bsodBuilder));
    }
}

public struct MySpriteContainer
{
    readonly string _spriteName;
    readonly Vector2 _size;
    readonly Vector2 _positionFromCenter;
    readonly float _rotationOrScale;
    readonly Color _color;
    readonly string _font;
    readonly string _text;
    readonly float _scale;
    readonly bool _isText;
    readonly TextAlignment _textAlign;
    readonly bool _fillWidth;

    public MySpriteContainer(string spriteName, Vector2 size, Vector2 positionFromCenter, float rotation, Color color, bool fillWidth = false)
    {
        _spriteName = spriteName;
        _size = size;
        _positionFromCenter = positionFromCenter;
        _rotationOrScale = rotation;
        _color = color;
        _isText = false;

        _font = "";
        _text = "";
        _scale = 0f;

        _textAlign = TextAlignment.CENTER;
        _fillWidth = fillWidth;
    }

    public MySpriteContainer(string text, string font, float scale, Vector2 positionFromCenter, Color color, TextAlignment textAlign = TextAlignment.CENTER)
    {
        _text = text;
        _font = font;
        _scale = scale;
        _positionFromCenter = positionFromCenter;
        _rotationOrScale = scale;
        _color = color;
        _isText = true;
        _textAlign = textAlign;

        _spriteName = "";
        _size = Vector2.Zero;
        _fillWidth = false;
    }

    public MySprite CreateSprite(float scale, ref Vector2 center, ref Vector2 viewportSize)
    {
        if (!_isText)
        {
            if (_fillWidth)
            {
                Vector2 sizeAdjusted = new Vector2(viewportSize.X, _size.Y * scale);
                return new MySprite(SpriteType.TEXTURE, _spriteName, center + _positionFromCenter * scale, sizeAdjusted, _color, rotation: _rotationOrScale);
            }
            return new MySprite(SpriteType.TEXTURE, _spriteName, center + _positionFromCenter * scale, _size * scale, _color, rotation: _rotationOrScale);
        }
        else
            return new MySprite(SpriteType.TEXT, _text, center + _positionFromCenter * scale, null, _color, _font, rotation: _rotationOrScale * scale, alignment: _textAlign);
    }
}

public interface IConfigValue
{
    void WriteToIni(ref MyIni ini, string section);
    bool ReadFromIni(ref MyIni ini, string section);
    bool Update(ref MyIni ini, string section);
    void Reset();
    string Name { get; set; }
    string Comment { get; set; }
}

public interface IConfigValue<T> : IConfigValue
{
    T Value { get; set; }
}

public abstract class ConfigValue<T> : IConfigValue<T>
{
    public string Name { get; set; }
    public string Comment { get; set; }
    protected T _value;
    public T Value
    {
        get { return _value; }
        set
        {
            _value = value;
            _skipRead = true;
        }
    }
    readonly T _defaultValue;
    bool _skipRead = false;

    public static implicit operator T(ConfigValue<T> cfg)
    {
        return cfg.Value;
    }

    public ConfigValue(string name, T defaultValue, string comment)
    {
        Name = name;
        _value = defaultValue;
        _defaultValue = defaultValue;
        Comment = comment;
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        if (_skipRead)
        {
            _skipRead = false;
            return true;
        }
        MyIniValue val = ini.Get(section, Name);
        bool read = !val.IsEmpty;
        if (read)
        {
            read = SetValue(ref val);
        }
        else
        {
            SetDefault();
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        ini.Set(section, Name, this.ToString());
        if (!string.IsNullOrWhiteSpace(Comment))
        {
            ini.SetComment(section, Name, Comment);
        }
        _skipRead = false;
    }

    public void Reset()
    {
        SetDefault();
        _skipRead = false;
    }

    protected abstract bool SetValue(ref MyIniValue val);

    protected virtual void SetDefault()
    {
        _value = _defaultValue;
    }
}

class ConfigSection
{
    public string Section { get; set; }
    public string Comment { get; set; }
    List<IConfigValue> _values = new List<IConfigValue>();

    public ConfigSection(string section, string comment = null)
    {
        Section = section;
        Comment = comment;
    }

    public void AddValue(IConfigValue value)
    {
        _values.Add(value);
    }

    public void AddValues(List<IConfigValue> values)
    {
        _values.AddRange(values);
    }

    public void AddValues(params IConfigValue[] values)
    {
        _values.AddRange(values);
    }

    void SetComment(ref MyIni ini)
    {
        if (!string.IsNullOrWhiteSpace(Comment))
        {
            ini.SetSectionComment(Section, Comment);
        }
    }

    public void ReadFromIni(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.ReadFromIni(ref ini, Section);
        }
    }

    public void WriteToIni(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.WriteToIni(ref ini, Section);
        }
        SetComment(ref ini);
    }

    public void Update(ref MyIni ini)
    {    
        foreach (IConfigValue c in _values)
        {
            c.Update(ref ini, Section);
        }
        SetComment(ref ini);
    }
}
public class ConfigInt : ConfigValue<int>
{
    public ConfigInt(string name, int value = 0, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetInt32(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigString : ConfigValue<string>
{
    public ConfigString(string name, string value = "", string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetString(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigEnum<TEnum> : ConfigValue<TEnum> where TEnum : struct
{
    public ConfigEnum(string name, TEnum defaultValue = default(TEnum), string comment = null)
    : base (name, defaultValue, comment)
    {}

    protected override bool SetValue(ref MyIniValue val)
    {
        string enumerationStr;
        if (!val.TryGetString(out enumerationStr) ||
            !Enum.TryParse(enumerationStr, true, out _value) ||
            !Enum.IsDefined(typeof(TEnum), _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigBool : ConfigValue<bool>
{
    public ConfigBool(string name, bool value = false, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetBoolean(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigFloat : ConfigValue<float>
{
    public ConfigFloat(string name, float value = 0, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetSingle(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigDouble : ConfigValue<double>
{
    public ConfigDouble(string name, double value = 0, string comment = null) : base(name, value, comment) { }
    protected override bool SetValue(ref MyIniValue val)
    {
        if (!val.TryGetDouble(out _value))
        {
            SetDefault();
            return false;
        }
        return true;
    }
}

public class ConfigColor : ConfigValue<Color>
{
    public ConfigColor(string name, Color value = default(Color), string comment = null) : base(name, value, comment) { }
    public override string ToString()
    {
        return string.Format("{0}, {1}, {2}, {3}", Value.R, Value.G, Value.B, Value.A);
    }
    protected override bool SetValue(ref MyIniValue val)
    {
        string rgbString = val.ToString("");
        string[] rgbSplit = rgbString.Split(',');

        int r = 0, g = 0, b = 0, a = 0;
        if (rgbSplit.Length != 4 ||
            !int.TryParse(rgbSplit[0].Trim(), out r) ||
            !int.TryParse(rgbSplit[1].Trim(), out g) ||
            !int.TryParse(rgbSplit[2].Trim(), out b))
        {
            SetDefault();
            return false;
        }

        bool hasAlpha = int.TryParse(rgbSplit[3].Trim(), out a);
        if (!hasAlpha)
        {
            a = 255;
        }

        r = MathHelper.Clamp(r, 0, 255);
        g = MathHelper.Clamp(g, 0, 255);
        b = MathHelper.Clamp(b, 0, 255);
        a = MathHelper.Clamp(a, 0, 255);
        _value = new Color(r, g, b, a);
        return true;
    }
}
public class ConfigDeprecated<T, ConfigImplementation> : IConfigValue where ConfigImplementation : IConfigValue<T>, IConfigValue
{
    public readonly ConfigImplementation Implementation;
    public Action<T> Callback;

    public string Name 
    { 
        get { return Implementation.Name; }
        set { Implementation.Name = value; }
    }

    public string Comment 
    { 
        get { return Implementation.Comment; } 
        set { Implementation.Comment = value; } 
    }

    public ConfigDeprecated(ConfigImplementation impl)
    {
        Implementation = impl;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        bool read = Implementation.ReadFromIni(ref ini, section);
        if (read)
        {
            Callback?.Invoke(Implementation.Value);
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        ini.Delete(section, Implementation.Name);
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public void Reset() {}
}

public class ConfigNullable<T, ConfigImplementation> : IConfigValue<T>, IConfigValue
    where ConfigImplementation : IConfigValue<T>, IConfigValue
    where T : struct
{
    public string Name 
    { 
        get { return Implementation.Name; }
        set { Implementation.Name = value; }
    }

    public string Comment 
    { 
        get { return Implementation.Comment; } 
        set { Implementation.Comment = value; } 
    }
    
    public string NullString;
    public T Value
    {
        get { return Implementation.Value; }
        set 
        { 
            Implementation.Value = value;
            HasValue = true;
            _skipRead = true;
        }
    }
    public readonly ConfigImplementation Implementation;
    public bool HasValue { get; private set; }
    bool _skipRead = false;

    public ConfigNullable(ConfigImplementation impl, string nullString = "none")
    {
        Implementation = impl;
        NullString = nullString;
        HasValue = false;
    }

    public void Reset()
    {
        HasValue = false;
        _skipRead = true;
    }

    public bool ReadFromIni(ref MyIni ini, string section)
    {
        if (_skipRead)
        {
            _skipRead = false;
            return true;
        }
        bool read = Implementation.ReadFromIni(ref ini, section);
        if (read)
        {
            HasValue = true;
        }
        else
        {
            HasValue = false;
        }
        return read;
    }

    public void WriteToIni(ref MyIni ini, string section)
    {
        Implementation.WriteToIni(ref ini, section);
        if (!HasValue)
        {
            ini.Set(section, Implementation.Name, NullString);
        }
    }

    public bool Update(ref MyIni ini, string section)
    {
        bool read = ReadFromIni(ref ini, section);
        WriteToIni(ref ini, section);
        return read;
    }

    public override string ToString()
    {
        return HasValue ? Value.ToString() : NullString;
    }
}

/// <summary>
/// Selects the active controller from a list using the following priority:
/// Main controller > Oldest controlled ship controller > Any controlled ship controller.
/// </summary>
/// <param name="controllers">List of ship controlers</param>
/// <param name="lastController">Last actively controlled controller</param>
/// <returns>Actively controlled ship controller or null if none is controlled</returns>
IMyShipController GetControlledShipController(List<IMyShipController> controllers, IMyShipController lastController = null)
{
    IMyShipController currentlyControlled = null;
    foreach (IMyShipController ctrl in controllers)
    {
        if (ctrl.IsMainCockpit)
        {
            return ctrl;
        }

        // Grab the first seat that has a player sitting in it
        // and save it away in-case we don't have a main contoller
        if (currentlyControlled == null && ctrl != lastController && ctrl.IsUnderControl && ctrl.CanControlShip)
        {
            currentlyControlled = ctrl;
        }
    }

    // We did not find a main controller, so if the first controlled controller
    // from last cycle if it is still controlled
    if (lastController != null && lastController.IsUnderControl)
    {
        return lastController;
    }

    // Otherwise we return the first ship controller that we
    // found that was controlled.
    if (currentlyControlled != null)
    {
        return currentlyControlled;
    }

    // Nothing is under control, return the controller from last cycle.
    return lastController;
}
#endregion
    }
}
