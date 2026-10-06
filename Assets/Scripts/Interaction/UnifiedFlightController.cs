using System;
using Planet.Procedural.Terrain;
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
        public float speedResponseSeconds = 0.35f;
        public int initialSpeedLevel = 3;
        public double collisionRadiusMetres = 0.5;
        public bool handleInput = true;

        public double3 PositionWorldMetres { get { return positionWorldMetres; } }
        public double3 VelocityWorldMetresPerSecond { get { return velocityWorldMetresPerSecond; } }
        public bool InNearSurface { get { return activeBodyIndex >= 0; } }
        public int NearbyBodyIndex { get { return nearbyBodyIndex; } }
        public int NavigationTargetIndex { get { return navigationTargetIndex; } }
        public bool IsAutoFocusing { get { return autoFocusing; } }
        public double3 PositionBodyFixedMetres { get { return bodyFixedMetres; } }
        public double3 VelocityBodyFixedMetresPerSecond { get { return velocityBodyFixedMetresPerSecond; } }
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
        private double3 focusStart;
        private Quaternion focusStartLook;
        private float focusElapsed, focusDuration;
        private double3 wheelVelocityWorld;
        private int translationFrameIndex = -1;
        private double3 previousFrameCenter;
        private bool restoringBodyScale;

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
        }

        private void OnDisable()
        {
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
            UpdateNearGate();
            UpdateNearbyBody();
        }

        private void Update()
        {
            if (simulation == null) simulation = SolarSystemSimulation.Instance;
            if (simulation == null || cameraRig == null || unityCamera == null || simulation.BodyCount == 0) return;
            if (!initialized) Initialize();
            if (!initialized) return;

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
            focusStart = positionWorldMetres;
            focusStartLook = lookWorld;
            focusElapsed = 0f;
            double radius = Math.Max(1.0, simulation.DefinitionAt(bodyIndex).RadiusKm * 1000.0);
            double range = math.distance(positionWorldMetres, BodyWorldMetres(bodyIndex));
            focusDuration = Mathf.Clamp((float)(0.6 + 0.3 * Math.Log10(Math.Max(range / radius, 1.0))), 0.6f, 2.5f);
            wheelVelocityWorld = double3.zero;
        }

        public void CancelNavigation()
        {
            autoFocusing = false;
            navigationTargetIndex = -1;
            wheelVelocityWorld = double3.zero;
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
            double3 outward = math.normalizesafe(focusStart - body, new double3(0, 0, -1));
            double radius = simulation.DefinitionAt(navigationTargetIndex).RadiusKm * 1000.0;
            double frameRange = Math.Max(radius * 1.2,
                simulation.FrameDistanceFor(navigationTargetIndex) * simulation.KmPerUnit * 1000.0);
            double3 destination = body + outward * frameRange;
            double3 previous = positionWorldMetres;
            focusElapsed += dt;
            float t = Mathf.Clamp01(focusElapsed / focusDuration);
            float eased = t * t * (3f - 2f * t);
            positionWorldMetres = math.lerp(focusStart, destination, eased);
            velocityWorldMetresPerSecond = (positionWorldMetres - previous) / dt;
            Vector3 forward = ToVector3(math.normalizesafe(body - positionWorldMetres, new double3(0, 0, 1)));
            Quaternion destinationLook = Quaternion.LookRotation(forward, Vector3.up);
            lookWorld = Quaternion.Slerp(focusStartLook, destinationLook, eased);
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
            if (mouse == null || !mouse.rightButton.isPressed || autoFocusing) return;
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

        private void AdvanceFlight(float dt)
        {
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            bool rmb = handleInput && mouse != null && mouse.rightButton.isPressed;
            double3 axes = kb == null ? double3.zero : SurfaceRoamInput.FlyLocalAxes(
                kb.wKey.isPressed, kb.aKey.isPressed, kb.sKey.isPressed, kb.dKey.isPressed,
                kb.qKey.isPressed, kb.eKey.isPressed, rmb);
            double speed = SurfaceRoamInput.SpeedForLevel(speedLevel);
            if (activeBodyIndex >= 0)
            {
                double altitude = CurrentDisplayedClearance();
                double radius = simulation.DefinitionAt(activeBodyIndex).RadiusKm * 1000.0;
                double ceiling = SurfaceRoamSpeed.CeilingAltitudeMetres(radius);
                speed = Math.Min(speed, SurfaceRoamSpeed.TargetSpeedMetresPerSecond(
                    altitude, ceiling, SurfaceRoamSpeed.CeilingSpeedForRadius(radius)));
            }

            double3 targetVelocity = double3.zero;
            if (math.lengthsq(axes) > 0)
            {
                Vector3 direction = lookWorld * new Vector3((float)axes.x, (float)axes.y, (float)axes.z);
                targetVelocity = math.normalizesafe((double3)(float3)direction) * speed;
            }

            float scroll = handleInput && mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (handleInput && kb != null && activeBodyIndex < 0 && navigationTargetIndex >= 0)
            {
                if (kb.pageUpKey.isPressed || kb.zKey.isPressed) scroll -= 1100f * dt;
                if (kb.pageDownKey.isPressed || kb.xKey.isPressed) scroll += 1100f * dt;
            }
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (navigationTargetIndex < 0 || activeBodyIndex >= 0)
                    speedLevel = SurfaceRoamInput.AdjustSpeedLevel(speedLevel, scroll / 120f);
                else
                {
                    double3 radial = positionWorldMetres - BodyWorldMetres(navigationTargetIndex);
                    double range = math.length(radial);
                    double logDelta = math.clamp(scroll * 0.002, -2.0, 2.0);
                    wheelVelocityWorld = radial / Math.Max(range, 1.0) *
                        ((Math.Exp(logDelta) - 1.0) * range / Math.Max(speedResponseSeconds, 0.01f));
                }
            }
            if (navigationTargetIndex >= 0 && activeBodyIndex < 0) targetVelocity += wheelVelocityWorld;
            wheelVelocityWorld = math.lerp(wheelVelocityWorld, double3.zero,
                1.0 - Math.Exp(-dt / Math.Max(speedResponseSeconds, 0.01f)));

            if (activeBodyIndex >= 0)
            {
                Quaternion bodyRotation = simulation.ViewAt(activeBodyIndex).transform.rotation;
                double3 targetBodyVelocity = Rotate(Quaternion.Inverse(bodyRotation), targetVelocity);
                double accel = Math.Max(60.0, math.max(speed, math.length(velocityBodyFixedMetresPerSecond)) /
                    Math.Max(speedResponseSeconds, 0.01f));
                StepVelocity(ref velocityBodyFixedMetresPerSecond, targetBodyVelocity, accel, dt);
                double3 previous = bodyFixedMetres;
                bodyFixedMetres += velocityBodyFixedMetresPerSecond * dt;
                ResolveBodyCollision(previous);
                RefreshBodyFrame();
            }
            else
            {
                BrakeForApproach(ref targetVelocity);
                double accel = Math.Max(60.0, math.max(math.length(targetVelocity),
                    math.length(velocityWorldMetresPerSecond)) / Math.Max(speedResponseSeconds, 0.01f));
                StepVelocity(ref velocityWorldMetresPerSecond, targetVelocity, accel, dt);
                double3 previous = positionWorldMetres;
                positionWorldMetres += velocityWorldMetresPerSecond * dt;
                if (!EnterAlongMovement(previous, dt))
                {
                    SweepApproach(previous);
                    ResolveApproachCollision();
                }
            }
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
            lookBody = inverse * lookWorld;
            activeBodyIndex = index;
            wheelVelocityWorld = double3.zero;
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
