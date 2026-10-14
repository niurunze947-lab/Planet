using System;
using Planet.Procedural.Terrain;
using Planet.Procedural;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Planet.Interaction
{
    /// <summary>Single movement owner in stellar space and, near a body, its rotating metre frame.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class UnifiedFlightController : MonoBehaviour
    {
        public SolarSystemCamera cameraRig;
        public float lookSensitivity = SurfaceRoamInput.DefaultLookSensitivity;
        [Tooltip("Maximum logarithmic distance change per second during automatic focus.")]
        [Min(0.1f)] public float focusLogRate = 4f;
        public float speedResponseSeconds = 0.35f;
        public int initialSpeedLevel = 3;
        public const float MinSpeedMultiplier = 1f / 32f;
        public const float MaxSpeedMultiplier = 128f;
        [SerializeField, Range(MinSpeedMultiplier, MaxSpeedMultiplier)]
        private float speedMultiplier = 1f;
        public float SpeedMultiplier { get { return Mathf.Clamp(speedMultiplier, MinSpeedMultiplier, MaxSpeedMultiplier); } }
        public double collisionRadiusMetres = 0.5;
        public bool handleInput = true;
        public bool allowOrbitInput = true;
        public Func<bool> PointerOverTool { get; set; }
        [Tooltip("Apply GM/r^2 of the nearby planet in real seconds. B toggles gravity.")]
        public bool gravityEnabled;
        public bool GravityEnabled { get { return gravityEnabled; } }

        public double3 PositionWorldMetres { get { return positionWorldMetres; } }
        public double3 VelocityWorldMetresPerSecond { get { return velocityWorldMetresPerSecond; } }
        public bool InNearSurface { get { return activeBodyIndex >= 0; } }
        public int NearbyBodyIndex { get { return nearbyBodyIndex; } }
        public int NavigationTargetIndex { get { return navigationTargetIndex; } }
        public bool IsAutoFocusing { get { return autoFocusing; } }
        public double3 PositionBodyFixedMetres { get { return bodyFixedMetres; } }
        public double3 VelocityBodyFixedMetresPerSecond { get { return velocityBodyFixedMetresPerSecond; } }
        public double MovementSpeedLimitMetresPerSecond { get { return FlightSpeedLimit(); } }
        public bool CollisionCoverageReady
        {
            get { return ActiveCollisionQuery() != null && ActiveCollisionQuery().CollisionCoverageReady; }
        }

        private SolarSystemSimulation simulation;
        private Camera unityCamera;
        private ObserverRenderRig renderRig;
        private bool initialized, autoFocusing;
        private int activeBodyIndex = -1, nearbyBodyIndex = -1, navigationTargetIndex = -1;
        private int speedLevel;
        private double3 positionWorldMetres, velocityWorldMetresPerSecond;
        private double3 bodyFixedMetres, velocityBodyFixedMetresPerSecond;
        private Quaternion lookWorld = Quaternion.identity, lookBody = Quaternion.identity;
        private double3 focusDirection;
        private double focusStartRange, focusEndRange;
        private Quaternion focusStartLook;
        private double focusElapsed, focusDuration;
        private double3 wheelVelocityWorld;
        private int translationFrameIndex = -1;
        private double3 previousFrameCenter;
        private bool restoringBodyScale;
        private double3 gravityVelocityWorld, gravityVelocityBody;
        private bool appliedGravityState;

        private void Awake()
        {
            if (cameraRig == null) cameraRig = GetComponent<SolarSystemCamera>();
            unityCamera = GetComponent<Camera>();
            renderRig = GetComponent<ObserverRenderRig>();
            if (renderRig == null) renderRig = gameObject.AddComponent<ObserverRenderRig>();
            foreach (var legacy in GetComponents<MonoBehaviour>())
                if (legacy is SurfaceRoamController || legacy is SurfaceModeController ||
                    legacy is BodyFixedCameraAnchor || legacy is ViewDistanceController) legacy.enabled = false;
            speedLevel = initialSpeedLevel;
            appliedGravityState = gravityEnabled;
        }

        private void OnEnable() { SurfaceRoamHudState.GravityChanged += SetGravityEnabled; }

        private void OnDisable()
        {
            SurfaceRoamHudState.GravityChanged -= SetGravityEnabled;
            if (cameraRig != null)
            {
                cameraRig.externalObserver = false;
                cameraRig.freeRoam = false;
                cameraRig.handleInput = true;
            }
            if (simulation != null) simulation.ClearObserver();
            SurfaceRoamHudState.Clear();
        }

        /// <summary>Explicit navigation reset for captures and spawn points; never used for normal flight.</summary>
        public void SetPose(double3 stellarMetres, Quaternion orientation, double3 velocityMetresPerSecond)
        {
            if (!math.all(math.isfinite(stellarMetres)) || !math.all(math.isfinite(velocityMetresPerSecond)))
                throw new ArgumentException("Observer pose must be finite.");
            if (simulation == null) simulation = SolarSystemSimulation.Instance;
            if (!initialized) Initialize();
            activeBodyIndex = -1;
            translationFrameIndex = -1;
            navigationTargetIndex = -1;
            autoFocusing = false;
            positionWorldMetres = stellarMetres;
            velocityWorldMetresPerSecond = velocityMetresPerSecond;
            lookWorld = orientation;
            wheelVelocityWorld = double3.zero;
            gravityVelocityWorld = gravityVelocityBody = double3.zero;
            UpdateNearGate();
            UpdateNearbyBody();
        }

        private void Update()
        {
            if (simulation == null) simulation = SolarSystemSimulation.Instance;
            if (simulation == null || cameraRig == null || unityCamera == null || simulation.BodyCount == 0) return;
            if (!initialized) Initialize();
            if (!initialized) return;
            if (appliedGravityState != gravityEnabled) SetGravityEnabled(gravityEnabled);

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;

            if (activeBodyIndex >= 0) RefreshBodyFrame();
            else if (!autoFocusing && translationFrameIndex >= 0)
            {
                double3 nextCenter = BodyWorldMetres(translationFrameIndex);
                positionWorldMetres += nextCenter - previousFrameCenter;
                previousFrameCenter = nextCenter;
                double radius = simulation.DefinitionAt(translationFrameIndex).RadiusKm * 1000.0;
                if (math.distance(positionWorldMetres, nextCenter) > radius * 12.0) translationFrameIndex = -1;
            }
            HandleCommands();
            HandleLook();
            HandleOrbit(dt);
            HandleNavigationScroll(dt);
            if (autoFocusing) AdvanceFocus(dt);
            else AdvanceFlight(dt);
            UpdateNearGate();
            UpdateNearbyBody();
            if (!autoFocusing && translationFrameIndex < 0)
            {
                int candidate = ClosestBodyIndex(out double ratio, out double ignored);
                if (candidate >= 0 && ratio < 0.875)
                {
                    translationFrameIndex = candidate;
                    previousFrameCenter = BodyWorldMetres(candidate);
                }
            }
        }

        private void Initialize()
        {
            cameraRig.ApplyTransform();
            Vec3d origin = simulation.OriginKm;
            double metresPerUnit = simulation.KmPerUnit * 1000.0;
            Vector3 cameraPosition = transform.position;
            positionWorldMetres = new double3(origin.x * 1000.0 + cameraPosition.x * metresPerUnit,
                origin.z * 1000.0 + cameraPosition.y * metresPerUnit,
                -origin.y * 1000.0 + cameraPosition.z * metresPerUnit);
            lookWorld = transform.rotation;
            cameraRig.externalObserver = true;
            cameraRig.freeRoam = true;
            cameraRig.handleInput = false;
            cameraRig.handleZoomInput = false;
            initialized = true;
            UpdateNearbyBody();
            UpdateNearGate();
            translationFrameIndex = simulation.FocusIndex;
            previousFrameCenter = BodyWorldMetres(translationFrameIndex);
        }

        private void HandleCommands()
        {
            if (!handleInput) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            int speedStep = (kb.numpadPlusKey.wasPressedThisFrame ? 1 : 0) -
                (kb.numpadMinusKey.wasPressedThisFrame ? 1 : 0);
            if (speedStep != 0) AdjustSpeedMultiplier(speedStep);
            for (int i = 0; i < SolarSystemCamera.HotkeyBodies.Length; i++)
            {
                Key key = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (!kb[key].wasPressedThisFrame) continue;
                simulation.SetFocusByName(SolarSystemCamera.HotkeyBodies[i], false);
                BeginFocus(simulation.FocusIndex);
                return;
            }
            if (kb.tabKey.wasPressedThisFrame)
            {
                simulation.CycleFocus(kb.shiftKey.isPressed ? -1 : 1);
                BeginFocus(simulation.FocusIndex);
            }
            if (kb.fKey.wasPressedThisFrame) BeginFocus(simulation.FocusIndex);
            if (kb.bKey.wasPressedThisFrame) SetGravityEnabled(!gravityEnabled);
            if (kb.escapeKey.wasPressedThisFrame)
                CancelNavigation();
            if (kb.gKey.wasPressedThisFrame && activeBodyIndex < 0 && nearbyBodyIndex < 0 && !restoringBodyScale)
                simulation.bodySizeExaggeration = Mathf.Approximately(simulation.bodySizeExaggeration, 1f) ? 400f : 1f;
            if (kb.lKey.wasPressedThisFrame)
                SolarMaterials.SetGridStrength(SolarMaterials.CurrentGridStrength > 0.001f ? 0f : SolarMaterials.GridStrength);
        }

        public void BeginFocus(int bodyIndex)
        {
            if (simulation == null || bodyIndex < 0 || bodyIndex >= simulation.BodyCount) return;
            if (activeBodyIndex >= 0) LeaveBodyFrame();
            navigationTargetIndex = bodyIndex;
            autoFocusing = true;
            translationFrameIndex = -1;
            double3 relative = positionWorldMetres - BodyWorldMetres(bodyIndex);
            focusStartRange = Math.Max(0.01, math.length(relative));
            focusDirection = math.normalizesafe(relative, new double3(0, 0, -1));
            focusStartLook = lookWorld;
            focusElapsed = 0f;
            double radius = Math.Max(1.0, simulation.DefinitionAt(bodyIndex).RadiusKm * 1000.0);
            focusEndRange = Math.Max(radius * 1.2,
                simulation.FrameDistanceFor(bodyIndex) * simulation.KmPerUnit * 1000.0);
            focusDuration = FlightMotion.FocusDuration(focusStartRange, focusEndRange, focusLogRate);
            wheelVelocityWorld = double3.zero;
            gravityVelocityWorld = gravityVelocityBody = double3.zero;
        }

        public void SetGravityEnabled(bool enabled)
        {
            if (appliedGravityState == enabled) return;
            gravityEnabled = enabled;
            appliedGravityState = enabled;
            // Preserve total velocity when toggling; only the decomposition changes.
            gravityVelocityWorld = gravityVelocityBody = double3.zero;
            SurfaceRoamHudState.GravityEnabled = enabled;
        }

        public void CancelNavigation()
        {
            autoFocusing = false;
            navigationTargetIndex = -1;
            wheelVelocityWorld = double3.zero;
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                throw new ArgumentException("Speed multiplier must be finite.");
            speedMultiplier = Mathf.Clamp(multiplier, MinSpeedMultiplier, MaxSpeedMultiplier);
        }

        public void AdjustSpeedMultiplier(int steps)
        {
            SetSpeedMultiplier(SpeedMultiplier * Mathf.Pow(2f, Mathf.Clamp(steps, -12, 12)));
        }

        private void AdvanceFocus(float dt)
        {
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            bool manual = handleInput && mouse != null && mouse.rightButton.isPressed && kb != null &&
                (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed ||
                 kb.qKey.isPressed || kb.eKey.isPressed);
            if (manual) { autoFocusing = false; return; }

            double3 body = BodyWorldMetres(navigationTargetIndex);
            focusElapsed += dt;
            double t = Math.Min(1, focusElapsed / focusDuration);
            double range = FlightMotion.FocusRange(focusStartRange, focusEndRange, t, focusDuration,
                out double radialVelocity);
            positionWorldMetres = body + focusDirection * range;
            // Velocity is relative to the target's translating frame, not its accelerated ephemeris.
            velocityWorldMetresPerSecond = focusDirection * radialVelocity;
            Quaternion destinationLook = SolarSystemCamera.StableLookRotation(
                ToVector3(-focusDirection), focusStartLook * Vector3.up, Vector3.up);
            float turn = Mathf.SmoothStep(0, 1, (float)(focusElapsed / Math.Min(0.6, focusDuration)));
            lookWorld = Quaternion.Slerp(focusStartLook, destinationLook, turn);
            if (t >= 1f)
            {
                autoFocusing = false;
                velocityWorldMetresPerSecond = double3.zero;
                translationFrameIndex = navigationTargetIndex;
                previousFrameCenter = body;
            }
        }

        private void HandleLook()
        {
            if (!handleInput) return;
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed || autoFocusing || PointerOverTool?.Invoke() == true) return;
            Vector2 delta = SurfaceRoamInput.LookDeltaDegrees(mouse.delta.ReadValue(), true, lookSensitivity);
            if (activeBodyIndex < 0)
                lookWorld = SurfaceRoamController.ApplyLookDelta(lookWorld, Vector3.up, delta);
            else
            {
                Vector3 up = ToVector3(math.normalizesafe(bodyFixedMetres, new double3(0, 1, 0)));
                lookBody = SurfaceRoamController.ApplyLookDelta(lookBody, up, delta);
                lookWorld = simulation.ViewAt(activeBodyIndex).transform.rotation * lookBody;
            }
        }

        private void HandleOrbit(float dt)
        {
            Mouse mouse = Mouse.current;
            if (!handleInput || !allowOrbitInput || PointerOverTool?.Invoke() == true || mouse == null || !mouse.leftButton.isPressed || mouse.rightButton.isPressed) return;
            OrbitAroundTarget(mouse.delta.ReadValue(), dt);
        }

        /// <summary>Left drag changes the landing direction at the current radius, within the flight owner.</summary>
        public void OrbitAroundTarget(Vector2 mouseDelta, float dt)
        {
            if (!initialized || navigationTargetIndex < 0 || dt <= 0 || mouseDelta.sqrMagnitude < 1e-8f) return;
            if (activeBodyIndex >= 0 && activeBodyIndex != navigationTargetIndex) return;
            autoFocusing = false;
            double3 center = BodyWorldMetres(navigationTargetIndex);
            Quaternion bodyRotation = simulation.ViewAt(navigationTargetIndex).transform.rotation;
            double3 offset = activeBodyIndex >= 0 ? Rotate(bodyRotation, bodyFixedMetres) : positionWorldMetres - center;
            double range = math.length(offset);
            if (range < 1e-6) return;
            Quaternion facing = SolarSystemCamera.StableLookRotation(ToVector3(-offset / range),
                lookWorld * Vector3.up, Vector3.up);
            Quaternion desiredLook = SurfaceRoamController.ApplyLookDelta(facing, Vector3.up,
                mouseDelta * cameraRig.dragSensitivity);
            Quaternion rotation = desiredLook * Quaternion.Inverse(facing);
            double angle = Quaternion.Angle(Quaternion.identity, rotation) * Math.PI / 180.0;
            double maxAngle = FlightSpeedLimit() * dt / range;
            if (angle > maxAngle) rotation = Quaternion.Slerp(Quaternion.identity, rotation, (float)(maxAngle / angle));
            double3 nextOffset = Rotate(rotation, offset);
            lookWorld = rotation * facing;
            velocityWorldMetresPerSecond = Rotate(rotation, velocityWorldMetresPerSecond);
            gravityVelocityWorld = Rotate(rotation, gravityVelocityWorld);
            wheelVelocityWorld = Rotate(rotation, wheelVelocityWorld);
            if (activeBodyIndex >= 0)
            {
                double3 previous = bodyFixedMetres;
                bodyFixedMetres = Rotate(Quaternion.Inverse(bodyRotation), nextOffset);
                velocityBodyFixedMetresPerSecond = Rotate(Quaternion.Inverse(bodyRotation), velocityWorldMetresPerSecond);
                gravityVelocityBody = Rotate(Quaternion.Inverse(bodyRotation), gravityVelocityWorld);
                lookBody = Quaternion.Inverse(bodyRotation) * lookWorld;
                ResolveBodyCollision(previous);
                RefreshBodyFrame();
            }
            else
            {
                positionWorldMetres = center + nextOffset;
                ResolveApproachCollision();
                translationFrameIndex = navigationTargetIndex;
                previousFrameCenter = center;
            }
        }

        /// <summary>The single wheel/key input path; its impulse is already inside the terrain speed envelope.</summary>
        public void ApplyNavigationScroll(float scroll)
        {
            if (!initialized || Math.Abs(scroll) <= 0.01f) return;
            autoFocusing = false;
            if (navigationTargetIndex < 0)
            {
                double3 forward = (double3)(float3)(lookWorld * Vector3.forward);
                wheelVelocityWorld -= forward * (scroll / 120.0) *
                    SurfaceRoamInput.SpeedForLevel(speedLevel) * SpeedMultiplier;
                wheelVelocityWorld = FlightMotion.LimitMagnitude(wheelVelocityWorld, FlightSpeedLimit());
                return;
            }
            double3 radial = activeBodyIndex == navigationTargetIndex
                ? Rotate(simulation.ViewAt(activeBodyIndex).transform.rotation, bodyFixedMetres)
                : positionWorldMetres - BodyWorldMetres(navigationTargetIndex);
            double range = math.length(radial);
            double logDelta = math.clamp(scroll * 0.002, -2.0, 2.0);
            double3 impulse = radial / Math.Max(range, 1.0) *
                ((Math.Exp(logDelta) - 1.0) * range / Math.Max(speedResponseSeconds, 0.01f)) * SpeedMultiplier;
            wheelVelocityWorld += impulse;
            wheelVelocityWorld = FlightMotion.LimitMagnitude(wheelVelocityWorld, FlightSpeedLimit());
        }

        private double FlightSpeedLimit()
        {
            if (!initialized || simulation == null) return double.PositiveInfinity;
            int index = activeBodyIndex;
            if (index < 0) index = ClosestBodyIndex(out double ignoredRatio, out double ignoredAltitude);
            if (index < 0) return double.PositiveInfinity;
            double radius = simulation.DefinitionAt(index).RadiusKm * 1000.0;
            Quaternion bodyRotation = simulation.ViewAt(index).transform.rotation;
            double3 local = activeBodyIndex >= 0 ? bodyFixedMetres :
                Rotate(Quaternion.Inverse(bodyRotation), positionWorldMetres - BodyWorldMetres(index));
            double range = math.length(local);
            double3 direction = math.normalizesafe(local, new double3(0, 1, 0));
            double ground = radius;
            if (range < radius * 1.5)
            {
                var query = simulation.ViewAt(index).GetComponent<IPlanetCollisionSurfaceQuery>();
                double surface = query != null ? query.SampleCollisionRadiusMetres(direction) : 0;
                if (surface > 0) ground = surface;
            }
            double3 localVelocity = activeBodyIndex >= 0 ? velocityBodyFixedMetresPerSecond :
                Rotate(Quaternion.Inverse(bodyRotation), velocityWorldMetresPerSecond);
            // Brake one response interval ahead, including outstanding wheel inertia before the gate.
            double approach = Math.Min(0, math.dot(localVelocity, direction));
            double height = Math.Max(0, range - ground + approach * Math.Max(speedResponseSeconds, 0.01f));
            return SurfaceRoamSpeed.FlightSpeedLimit(height, radius) * SpeedMultiplier;
        }

        private void HandleNavigationScroll(float dt)
        {
            if (!handleInput) return;
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            if (PointerOverTool?.Invoke() == true) return;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (kb != null)
            {
                if (kb.pageUpKey.isPressed || (kb.zKey.isPressed && !kb.ctrlKey.isPressed)) scroll -= 1100f * dt;
                if (kb.pageDownKey.isPressed || kb.xKey.isPressed) scroll += 1100f * dt;
            }
            ApplyNavigationScroll(scroll);
        }

        private void AdvanceFlight(float dt)
        {
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            bool rmb = handleInput && mouse != null && mouse.rightButton.isPressed;
            double3 axes = kb == null ? double3.zero : SurfaceRoamInput.FlyLocalAxes(
                kb.wKey.isPressed, kb.aKey.isPressed, kb.sKey.isPressed, kb.dKey.isPressed,
                kb.qKey.isPressed, kb.eKey.isPressed, rmb);
            double speed = SurfaceRoamInput.SpeedForLevel(speedLevel) * SpeedMultiplier;
            double speedLimit = FlightSpeedLimit();
            speed = Math.Min(speed, speedLimit);

            double3 targetVelocity = double3.zero;
            if (math.lengthsq(axes) > 0)
            {
                Vector3 direction = lookWorld * new Vector3((float)axes.x, (float)axes.y, (float)axes.z);
                targetVelocity = math.normalizesafe((double3)(float3)direction) * speed;
            }

            targetVelocity += wheelVelocityWorld;
            targetVelocity = FlightMotion.LimitMagnitude(targetVelocity, speedLimit);
            wheelVelocityWorld = math.lerp(wheelVelocityWorld, double3.zero,
                1.0 - Math.Exp(-dt / Math.Max(speedResponseSeconds, 0.01f)));
            double currentSpeed = activeBodyIndex >= 0 ? math.length(velocityBodyFixedMetresPerSecond) :
                math.length(velocityWorldMetresPerSecond);
            double accel = Math.Max(60.0, Math.Max(speed, Math.Max(math.length(targetVelocity), currentSpeed)) /
                Math.Max(speedResponseSeconds, 0.01f));

            if (activeBodyIndex >= 0)
            {
                Quaternion bodyRotation = simulation.ViewAt(activeBodyIndex).transform.rotation;
                double3 targetBodyVelocity = Rotate(Quaternion.Inverse(bodyRotation), targetVelocity);
                double3 controlledVelocity = velocityBodyFixedMetresPerSecond - gravityVelocityBody;
                StepVelocity(ref controlledVelocity, targetBodyVelocity, accel, dt);
                double3 gravity = GravityAcceleration(activeBodyIndex, bodyFixedMetres);
                gravityVelocityBody += gravity * dt;
                velocityBodyFixedMetresPerSecond = controlledVelocity + gravityVelocityBody;
                double3 previous = bodyFixedMetres;
                bodyFixedMetres += velocityBodyFixedMetresPerSecond * dt;
                ResolveBodyCollision(previous);
                RefreshBodyFrame();
            }
            else
            {
                BrakeForApproach(ref targetVelocity);
                double3 controlledVelocity = velocityWorldMetresPerSecond - gravityVelocityWorld;
                StepVelocity(ref controlledVelocity, targetVelocity, accel, dt);
                int gravityBody = ClosestBodyIndex(out double gravityRatio, out double ignoredHeight);
                double3 gravity = gravityBody >= 0 && gravityRatio < 0.5
                    ? Rotate(simulation.ViewAt(gravityBody).transform.rotation,
                        GravityAcceleration(gravityBody, Rotate(Quaternion.Inverse(simulation.ViewAt(gravityBody).transform.rotation),
                            positionWorldMetres - BodyWorldMetres(gravityBody))))
                    : double3.zero;
                gravityVelocityWorld += gravity * dt;
                velocityWorldMetresPerSecond = controlledVelocity + gravityVelocityWorld;
                double3 previous = positionWorldMetres;
                positionWorldMetres += velocityWorldMetresPerSecond * dt;
                if (!EnterAlongMovement(previous, dt))
                {
                    SweepApproach(previous);
                    ResolveApproachCollision();
                }
            }
        }

        private double3 GravityAcceleration(int bodyIndex, double3 offsetBf)
        {
            if (!gravityEnabled) return double3.zero;
            var surface = simulation.ViewAt(bodyIndex).GetComponent<PlanetSurfaceView>();
            double mass = surface != null && surface.definition != null ? surface.definition.physical.massKilograms : 0;
            return FlightMotion.GravityAcceleration(offsetBf, mass);
        }

        private static void RemoveIntoSurface(ref double3 velocity, double3 normal)
        {
            double inward = math.dot(velocity, normal);
            if (inward < 0) velocity -= normal * inward;
        }

        private void BrakeForApproach(ref double3 desiredVelocity)
        {
            int bodyIndex = ClosestBodyIndex(out double ratio, out double altitude);
            if (bodyIndex < 0 || ratio > 0.25) return;
            double3 radial = math.normalizesafe(positionWorldMetres - BodyWorldMetres(bodyIndex));
            double inward = -math.dot(desiredVelocity, radial);
            if (inward <= 0) return;
            double deceleration = Math.Max(60.0, math.length(velocityWorldMetresPerSecond) /
                Math.Max(speedResponseSeconds, 0.01f));
            double safeInward = FlightMotion.SafeApproachSpeed(altitude - collisionRadiusMetres, deceleration);
            if (inward > safeInward) desiredVelocity += radial * (inward - safeInward);
        }

        private void SweepApproach(double3 previous)
        {
            for (int i = 0; i < simulation.BodyCount; i++)
            {
                if (simulation.DefinitionAt(i).Kind == BodyKind.Star) continue;
                var terrain = simulation.ViewAt(i).GetComponent<RuntimeTerrainRenderer>();
                if (terrain != null && !terrain.Source.solid) continue;
                double3 center = BodyWorldMetres(i);
                double radius = simulation.DefinitionAt(i).RadiusKm * 1000.0;
                if (!FlightMotion.SphereEntry(previous - center, positionWorldMetres - center,
                    radius + collisionRadiusMetres, out double fraction)) continue;
                positionWorldMetres = math.lerp(previous, positionWorldMetres, math.max(0, fraction - 1e-8));
                double3 normal = math.normalizesafe(positionWorldMetres - center);
                double inward = math.dot(velocityWorldMetresPerSecond, normal);
                if (inward < 0) velocityWorldMetresPerSecond -= normal * inward;
                RemoveIntoSurface(ref gravityVelocityWorld, normal);
            }
        }

        private bool EnterAlongMovement(double3 previous, float dt)
        {
            int candidate = -1;
            double firstEntry = 1.0;
            for (int i = 0; i < simulation.BodyCount; i++)
            {
                double3 center = BodyWorldMetres(i);
                double radius = simulation.DefinitionAt(i).RadiusKm * 1000.0 /
                    (1.0 - ViewDistanceBoundary.DefaultSurfaceModeEnterRatio);
                if (!FlightMotion.SphereEntry(previous - center, positionWorldMetres - center,
                    radius, out double entry) || entry >= firstEntry) continue;
                candidate = i;
                firstEntry = entry;
            }
            if (candidate < 0) return false;
            positionWorldMetres = math.lerp(previous, positionWorldMetres, firstEntry);
            EnterBodyFrame(candidate);
            double3 previousBf = bodyFixedMetres;
            bodyFixedMetres += velocityBodyFixedMetresPerSecond * dt * (1.0 - firstEntry);
            ResolveBodyCollision(previousBf);
            RefreshBodyFrame();
            return true;
        }

        private void ResolveApproachCollision()
        {
            int bodyIndex = ClosestBodyIndex(out double ratio, out double altitude);
            if (bodyIndex < 0 || ratio > 0.05) return;
            if (simulation.DefinitionAt(bodyIndex).Kind == BodyKind.Star) return;
            double3 body = BodyWorldMetres(bodyIndex);
            Quaternion rotation = simulation.ViewAt(bodyIndex).transform.rotation;
            double3 local = Rotate(Quaternion.Inverse(rotation), positionWorldMetres - body);
            double length = math.length(local);
            if (length <= 0) return;
            var query = simulation.ViewAt(bodyIndex).GetComponent<IPlanetCollisionSurfaceQuery>();
            var terrain = simulation.ViewAt(bodyIndex).GetComponent<RuntimeTerrainRenderer>();
            if (terrain != null && !terrain.Source.solid) return;
            double floor = query != null ? query.SampleCollisionRadiusMetres(local / length) :
                simulation.DefinitionAt(bodyIndex).RadiusKm * 1000.0;
            floor += collisionRadiusMetres;
            if (length >= floor) return;
            double3 normal = local / length;
            positionWorldMetres = body + Rotate(rotation, normal * floor);
            double inward = math.dot(velocityWorldMetresPerSecond, Rotate(rotation, normal));
            if (inward < 0) velocityWorldMetresPerSecond -= Rotate(rotation, normal) * inward;
            RemoveIntoSurface(ref gravityVelocityWorld, Rotate(rotation, normal));
        }

        private void ResolveBodyCollision(double3 previous)
        {
            if (simulation.DefinitionAt(activeBodyIndex).Kind == BodyKind.Star) return;
            var query = ActiveCollisionQuery();
            var terrain = simulation.ViewAt(activeBodyIndex).GetComponent<RuntimeTerrainRenderer>();
            if (terrain != null && !terrain.Source.solid) return;
            if (query != null && query.SweepSphere(previous, bodyFixedMetres, collisionRadiusMetres,
                out double3 resolved, out double3 hitNormal))
            {
                bodyFixedMetres = resolved;
                double into = math.dot(velocityBodyFixedMetresPerSecond, hitNormal);
                if (into < 0) velocityBodyFixedMetresPerSecond -= hitNormal * into;
                RemoveIntoSurface(ref gravityVelocityBody, hitNormal);
            }
            double length = math.length(bodyFixedMetres);
            if (length <= 0) return;
            double3 normalBf = bodyFixedMetres / length;
            double floor = query != null ? query.SampleCollisionRadiusMetres(normalBf) :
                simulation.DefinitionAt(activeBodyIndex).RadiusKm * 1000.0;
            if (floor <= 0) return;
            floor += collisionRadiusMetres;
            if (length >= floor) return;
            bodyFixedMetres = normalBf * floor;
            double inward = math.dot(velocityBodyFixedMetresPerSecond, normalBf);
            if (inward < 0) velocityBodyFixedMetresPerSecond -= normalBf * inward;
            RemoveIntoSurface(ref gravityVelocityBody, normalBf);
        }

        private void UpdateNearGate()
        {
            if (autoFocusing) return;
            if (activeBodyIndex >= 0)
            {
                double3 fromBody = positionWorldMetres - BodyWorldMetres(activeBodyIndex);
                double range = math.length(fromBody);
                double radius = simulation.DefinitionAt(activeBodyIndex).RadiusKm * 1000.0;
                if (!FlightMotion.NearSurface(range, radius, true))
                    LeaveBodyFrame();
                return;
            }
            int candidate = ClosestBodyIndex(out double ratio, out double altitude);
            if (candidate >= 0 && ratio <= ViewDistanceBoundary.DefaultSurfaceModeEnterRatio)
            {
                double3 relative = Rotate(Quaternion.Inverse(simulation.ViewAt(candidate).transform.rotation),
                    positionWorldMetres - BodyWorldMetres(candidate));
                double range = math.length(relative);
                var query = simulation.ViewAt(candidate).GetComponent<IPlanetCollisionSurfaceQuery>();
                double surface = query != null ? query.SampleCollisionRadiusMetres(
                    math.normalizesafe(relative, new double3(0, 1, 0))) :
                    simulation.DefinitionAt(candidate).RadiusKm * 1000.0;
                if (range >= surface - 0.001) EnterBodyFrame(candidate);
            }
        }

        private void EnterBodyFrame(int index)
        {
            CelestialBody body = simulation.ViewAt(index);
            Quaternion inverse = Quaternion.Inverse(body.transform.rotation);
            bodyFixedMetres = Rotate(inverse, positionWorldMetres - BodyWorldMetres(index));
            velocityBodyFixedMetresPerSecond = Rotate(inverse, velocityWorldMetresPerSecond);
            gravityVelocityBody = Rotate(inverse, gravityVelocityWorld);
            lookBody = inverse * lookWorld;
            activeBodyIndex = index;
        }

        private void LeaveBodyFrame()
        {
            if (activeBodyIndex < 0) return;
            RefreshBodyFrame();
            translationFrameIndex = activeBodyIndex;
            previousFrameCenter = BodyWorldMetres(activeBodyIndex);
            activeBodyIndex = -1;
        }

        private void RefreshBodyFrame()
        {
            if (activeBodyIndex < 0) return;
            CelestialBody body = simulation.ViewAt(activeBodyIndex);
            Quaternion rotation = body.transform.rotation;
            positionWorldMetres = BodyWorldMetres(activeBodyIndex) + Rotate(rotation, bodyFixedMetres);
            velocityWorldMetresPerSecond = Rotate(rotation, velocityBodyFixedMetresPerSecond);
            gravityVelocityWorld = Rotate(rotation, gravityVelocityBody);
            lookWorld = rotation * lookBody;
        }

        private void UpdateNearbyBody()
        {
            int candidate = ClosestBodyIndex(out double ratio, out double altitude);
            nearbyBodyIndex = candidate >= 0 && ratio <= 0.25 ? candidate : -1;
            if (activeBodyIndex >= 0) nearbyBodyIndex = activeBodyIndex;
            double inflatedRatio = candidate >= 0
                ? math.distance(positionWorldMetres, BodyWorldMetres(candidate)) /
                    (simulation.DefinitionAt(candidate).RadiusKm * 1000.0 * simulation.bodySizeExaggeration)
                : double.PositiveInfinity;
            if ((nearbyBodyIndex >= 0 || inflatedRatio < 2.0) && simulation.bodySizeExaggeration > 1f)
                restoringBodyScale = true;
            if (restoringBodyScale)
            {
                simulation.bodySizeExaggeration = Mathf.MoveTowards(simulation.bodySizeExaggeration, 1f,
                    Math.Max(399f, simulation.bodySizeExaggeration * 2f) * Time.deltaTime);
                if (simulation.bodySizeExaggeration <= 1f) restoringBodyScale = false;
            }
        }

        private int ClosestBodyIndex(out double bestRatio, out double bestAltitude)
        {
            bestRatio = double.PositiveInfinity;
            bestAltitude = double.PositiveInfinity;
            int best = -1;
            for (int i = 0; i < simulation.BodyCount; i++)
            {
                Body definition = simulation.DefinitionAt(i);
                if (definition == null || definition.RadiusKm <= 0) continue;
                double distance = math.distance(positionWorldMetres, BodyWorldMetres(i));
                double altitude = distance - definition.RadiusKm * 1000.0;
                double ratio = altitude / Math.Max(distance, 1.0);
                if (ratio >= bestRatio) continue;
                best = i; bestRatio = ratio; bestAltitude = altitude;
            }
            return best;
        }

        private double CurrentDisplayedClearance()
        {
            if (activeBodyIndex < 0) return double.PositiveInfinity;
            double length = math.length(bodyFixedMetres);
            var query = ActiveCollisionQuery();
            double ground = query != null && length > 0
                ? query.SampleCollisionRadiusMetres(bodyFixedMetres / length)
                : simulation.DefinitionAt(activeBodyIndex).RadiusKm * 1000.0;
            return Math.Max(0.0, length - ground);
        }

        private IPlanetCollisionSurfaceQuery ActiveCollisionQuery()
        {
            return activeBodyIndex >= 0 ? simulation.ViewAt(activeBodyIndex).GetComponent<IPlanetCollisionSurfaceQuery>() : null;
        }

        private void LateUpdate()
        {
            if (!initialized || simulation == null) return;
            simulation.SetObserverWorldMetres(new Vec3d(positionWorldMetres.x,
                positionWorldMetres.y, positionWorldMetres.z), nearbyBodyIndex);
            simulation.SetObserverBodyFrame(activeBodyIndex,
                new Vec3d(bodyFixedMetres.x, bodyFixedMetres.y, bodyFixedMetres.z),
                new Vec3d(velocityBodyFixedMetresPerSecond.x, velocityBodyFixedMetresPerSecond.y,
                    velocityBodyFixedMetresPerSecond.z));
            simulation.RefreshObserverFrame();
            Vec3d origin = simulation.OriginKm;
            double metresPerUnit = simulation.KmPerUnit * 1000.0;
            double3 originWorld = new double3(origin.x * 1000.0, origin.z * 1000.0, -origin.y * 1000.0);
            transform.position = ToVector3((positionWorldMetres - originWorld) / metresPerUnit);
            transform.rotation = lookWorld;
            if (renderRig != null) renderRig.Sync();

            int measuredBody = nearbyBodyIndex >= 0 ? nearbyBodyIndex : simulation.FocusIndex;
            double distance = math.distance(positionWorldMetres, BodyWorldMetres(measuredBody));
            cameraRig.SetExternalViewHeight((float)(distance / metresPerUnit));
            ApplyProjection(distance, measuredBody, metresPerUnit);
            if (activeBodyIndex >= 0)
                SurfaceRoamHudState.Publish(true, CurrentDisplayedClearance(), math.length(velocityBodyFixedMetresPerSecond));
            else SurfaceRoamHudState.Clear();
            SurfaceRoamHudState.GravityEnabled = gravityEnabled;
            SurfaceRoamHudState.SpeedMultiplier = SpeedMultiplier;
        }

        private void ApplyProjection(double distanceMetres, int bodyIndex, double metresPerUnit)
        {
            double radius = simulation.DefinitionAt(bodyIndex).RadiusKm * 1000.0;
            double altitude = Math.Max(distanceMetres - radius, 1.0);
            float near = activeBodyIndex >= 0
                ? (float)(SurfaceCameraClip.NearMetres(altitude) / metresPerUnit)
                : (float)Math.Min(50.0, Math.Max(1e-7, altitude / metresPerUnit * 0.01));
            float far = activeBodyIndex >= 0
                ? (float)(SurfaceCameraClip.FarMetres(altitude, radius) / metresPerUnit)
                : 40000f;
            SolarSystemCamera.SanitizeProjectionPlanes(ref near, ref far);
            unityCamera.nearClipPlane = near;
            unityCamera.farClipPlane = far;
        }

        private double3 BodyWorldMetres(int index)
        {
            Vec3d p = simulation.PositionWorldMetresAt(index);
            return new double3(p.x, p.y, p.z);
        }

        private static double3 Rotate(Quaternion rotation, double3 value)
        {
            Vec3d result = SolarSystemSimulation.RotateMetres(rotation, new Vec3d(value.x, value.y, value.z));
            return new double3(result.x, result.y, result.z);
        }

        private static Vector3 ToVector3(double3 value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private static void StepVelocity(ref double3 velocity, double3 target, double acceleration, double dt)
        {
            velocity = FlightMotion.Accelerate(velocity, target, acceleration, dt);
        }
    }
}
