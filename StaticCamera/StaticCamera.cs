﻿using OWML.Common;
using OWML.ModHelper;
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StaticCamera;

public class StaticCamera : ModBehaviour
{
	public static OWCamera OWCamera { get; private set; }
	private Camera _camera;
	private GameObject _cameraObject;

	private bool _initNextTick = false;
	private bool _loaded = false;
	private bool _cameraOn = false;

	private ICommonCameraAPI _commonCameraAPI;

	public static StaticCamera Instance { get; private set; }

	private void Start()
	{
		ModHelper.Console.WriteLine($"{nameof(StaticCamera)} is loaded!", MessageType.Success);

		Instance = this;

		GlobalMessenger<OWCamera>.AddListener("SwitchActiveCamera", OnSwitchActiveCamera);
		GlobalMessenger<GraphicSettings>.AddListener("GraphicSettingsUpdated", OnGraphicSettingsUpdated);

		SceneManager.sceneLoaded += OnSceneLoaded;
		SceneManager.sceneUnloaded += OnSceneUnloaded;

		try
		{
			_commonCameraAPI = ModHelper.Interaction.TryGetModApi<ICommonCameraAPI>("xen.CommonCameraUtility");
		}
		catch (Exception e)
		{
			WriteError($"{e}");
		}
		finally
		{
			if (_commonCameraAPI == null)
			{
				WriteError($"CommonCameraAPI was not found. StaticCamera will not run.");
				enabled = false;
			}
		}
	}

	private void OnDestroy()
	{
		GlobalMessenger<OWCamera>.RemoveListener("SwitchActiveCamera", OnSwitchActiveCamera);
		GlobalMessenger<GraphicSettings>.RemoveListener("GraphicSettingsUpdated", OnGraphicSettingsUpdated);

		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneUnloaded -= OnSceneUnloaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "SolarSystem" && scene.name != "EyeOfTheUniverse") return;

		try
		{
			PreInit();
			_initNextTick = true;
		}
		catch (Exception e)
		{
			WriteError($"Failed static camera pre-initialization. {e.Message}. {e.StackTrace}.");
		}
	}

	private void OnSceneUnloaded(Scene _)
	{
		_loaded = false;
	}

	private void PreInit()
	{
		(OWCamera, _camera) = _commonCameraAPI.CreateCustomCamera("StaticCamera", (OWCamera cam) =>
		{
			cam.mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
			cam.mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("HeadsUpDisplay"));
			cam.mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("HelmetUVPass"));
		});

		_cameraObject = _camera.gameObject;
	}

	private void Init()
	{
		Locator.GetPlayerBody().gameObject.AddComponent<PromptHandler>();
	}

	private void PlaceCameraAtPosition()
	{
		OWRigidbody relativeBody = null;

		if (Locator.GetPlayerController().IsGrounded())
		{
			relativeBody = Locator.GetPlayerController()._groundBody;
		}
		if (relativeBody == null && (Time.time - Locator.GetPlayerController()._lastGroundedTime < 2f))
		{
			relativeBody = Locator.GetPlayerController()._lastGroundBody;
		}
		if (relativeBody == null)
		{
			relativeBody = Locator.GetReferenceFrame(true)?.GetOWRigidBody();
		}
		if (relativeBody == null)
		{
			relativeBody = Locator.GetReferenceFrame(false)?.GetOWRigidBody();
		}
		if (relativeBody == null)
		{
			WriteError("Couldn't find something to position the camera relative to.");
			Locator.GetPlayerAudioController().PlayNegativeUISound();
			return;
		}

		Write($"Putting camera relative to {relativeBody?.name}");

		_cameraObject.transform.parent = relativeBody.transform;
		_cameraObject.transform.position = Locator.GetActiveCamera().transform.position;
		_cameraObject.transform.rotation = Locator.GetActiveCamera().transform.rotation;

		_commonCameraAPI.EnterCamera(OWCamera);
	}

	private void OnSwitchActiveCamera(OWCamera camera)
	{
		if (camera.Equals(OWCamera) && !_cameraOn)
		{
			_cameraOn = true;

			GUIMode.SetRenderMode(GUIMode.RenderMode.Hidden);
		}
		else if (!camera.Equals(OWCamera) && _cameraOn)
		{
			_cameraOn = false;

			GUIMode.SetRenderMode(GUIMode.RenderMode.FPS);
		}
	}

	private void Update()
	{
		if (_initNextTick)
		{
			_initNextTick = false;
			try
			{
				Init();
			}
			catch (Exception e)
			{
				ModHelper.Console.WriteLine($"Failed static camera initialization. {e.Message}. {e.StackTrace}.", MessageType.Fatal);
			}
			_loaded = true;
		}

		if (!_loaded) return;

		bool toggleCamera = false;
		if (CanUse())
		{
			if (Keyboard.current != null)
			{
				toggleCamera |= Keyboard.current[Key.B].wasReleasedThisFrame;
			}

			// Only accept this when using gamepad
			if (OWInput.UsingGamepad())
			{
				toggleCamera |= OWInput.IsNewlyReleased(InputLibrary.toolOptionRight);
			}
		}

		if (toggleCamera)
		{
			if (_cameraOn)
			{
				_commonCameraAPI.ExitCamera(OWCamera);
				_cameraOn = false;
			}
			else
			{
				PlaceCameraAtPosition();

				// Update the FOV to make sure it matches the player camera
				_camera.fieldOfView = Locator.GetPlayerCamera().fieldOfView;
			}
		}
	}

	public bool CanUse()
	{
		// Always let us turn this off
		if (_cameraOn) return true;

		// Only allow use in certain input modes
		if (!OWInput.IsInputMode(InputMode.Character | InputMode.Dialogue | InputMode.ShipCockpit | InputMode.ModelShip | InputMode.LandingCam)) return false;

		// When using gamepad we're more restrictive
		if (OWInput.UsingGamepad())
		{
			var flag1 = (Locator.GetToolModeSwapper().GetToolMode() == ToolMode.SignalScope);
			var flag2 = (Locator.GetToolModeSwapper().GetToolMode() == ToolMode.Probe);
			return !flag1 && !flag2;
		}

		return true;
	}

	private void OnGraphicSettingsUpdated(GraphicSettings graphicsSettings)
	{
		if (!_loaded) return;

		if (OWMath.ApproxEquals(graphicsSettings.fieldOfView, _camera.fieldOfView, 0.001f))
		{
			return;
		}
		_camera.fieldOfView = graphicsSettings.fieldOfView;
	}

	public bool IsCameraOn()
	{
		return _cameraOn;
	}

	public void Write(string msg)
	{
		ModHelper.Console.WriteLine(msg, MessageType.Info);
	}

	public void WriteError(string msg)
	{
		ModHelper.Console.WriteLine(msg, MessageType.Error);
	}

	public void WriteWarning(string msg)
	{
		ModHelper.Console.WriteLine(msg, MessageType.Warning);
	}
}
