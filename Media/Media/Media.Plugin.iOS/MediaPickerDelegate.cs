//
//  Copyright 2011-2013, Xamarin Inc.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Media.Plugin.Abstractions;
using System.Runtime.InteropServices;
using System.Threading;
using CoreImage;
using Photos;
#if __UNIFIED__
using CoreGraphics;
using AssetsLibrary;
using Foundation;
using UIKit;
using NSAction = global::System.Action;
using CoreLocation;
using ImageIO;
#else
using MonoTouch.AssetsLibrary;
using MonoTouch.Foundation;
using MonoTouch.UIKit;

using CGRect = global::System.Drawing.RectangleF;
using nfloat = global::System.Single;
#endif

namespace Media.Plugin
{
	internal class MediaPickerDelegate
		: UIImagePickerControllerDelegate
	{
		internal MediaPickerDelegate (UIViewController viewController, UIImagePickerControllerSourceType sourceType, StoreCameraMediaOptions options)
		{
			this.viewController = viewController;
			this.source = sourceType;
			this.options = options ?? new StoreCameraMediaOptions();

			if (viewController != null) {
				UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications();
				this.observer = NSNotificationCenter.DefaultCenter.AddObserver (UIDevice.OrientationDidChangeNotification, DidRotate);
			}
		}
		
		public UIPopoverController Popover
		{
			get;
			set;
		}
		
		public UIView View
		{
			get { return this.viewController.View; }
		}

		public Task<MediaFile> Task
		{
			get { return tcs.Task; }
		}

		public override async void FinishedPickingMedia (UIImagePickerController picker, NSDictionary info)
		{
			MediaFile mediaFile;
			switch ((NSString)info[UIImagePickerController.MediaType])
			{
                case MediaImplementation.TypeImage:
					mediaFile = await GetPictureMediaFileAsync (info);
					break;

                case MediaImplementation.TypeMovie:
					mediaFile = GetMovieMediaFile (info);
					break;

				default:
					throw new NotSupportedException();
			}

			Dismiss (picker, () => this.tcs.TrySetResult (mediaFile));
		}

		public override void Canceled (UIImagePickerController picker)
		{
			Dismiss (picker, () => this.tcs.SetResult(null));
		}

		public void DisplayPopover (bool hideFirst = false)
		{
			if (Popover == null)
				return;

			var swidth = UIScreen.MainScreen.Bounds.Width;
			var sheight= UIScreen.MainScreen.Bounds.Height;

			nfloat width = 400;
			nfloat height = 300;

			if (this.orientation == null)
			{
				if (IsValidInterfaceOrientation (UIDevice.CurrentDevice.Orientation))
					this.orientation = UIDevice.CurrentDevice.Orientation;
				else
					this.orientation = GetDeviceOrientation (this.viewController.InterfaceOrientation);
			}

			nfloat x, y;
			if (this.orientation == UIDeviceOrientation.LandscapeLeft || this.orientation == UIDeviceOrientation.LandscapeRight)
			{
				y = (swidth / 2) - (height / 2);
				x = (sheight / 2) - (width / 2);
			}
			else
			{
				x = (swidth / 2) - (width / 2);
				y = (sheight / 2) - (height / 2);
			}

			if (hideFirst && Popover.PopoverVisible)
				Popover.Dismiss (animated: false);

			Popover.PresentFromRect (new CGRect (x, y, width, height), View, 0, animated: true);
		}

		private UIDeviceOrientation? orientation;
		private NSObject observer;
		private readonly UIViewController viewController;
		private readonly UIImagePickerControllerSourceType source;
		private readonly TaskCompletionSource<MediaFile> tcs = new TaskCompletionSource<MediaFile>();
		private readonly StoreCameraMediaOptions options;

		private bool IsCaptured
		{
			get { return this.source == UIImagePickerControllerSourceType.Camera; }
		}
		
		private void Dismiss (UIImagePickerController picker, NSAction onDismiss)
		{
			if (this.viewController == null)
				onDismiss();
			else {
				NSNotificationCenter.DefaultCenter.RemoveObserver (this.observer);
				UIDevice.CurrentDevice.EndGeneratingDeviceOrientationNotifications();

				this.observer.Dispose();

				if (Popover != null) {
					Popover.Dismiss (animated: true);
					Popover.Dispose();
					Popover = null;

					onDismiss();
				} else {
					picker.DismissViewController (true, onDismiss);
					picker.Dispose();
				}
			}
		}

		private void DidRotate (NSNotification notice)
		{
			UIDevice device = (UIDevice)notice.Object;
			if (!IsValidInterfaceOrientation (device.Orientation) || Popover == null)
				return;
			if (this.orientation.HasValue && IsSameOrientationKind (this.orientation.Value, device.Orientation))
				return;

			if (UIDevice.CurrentDevice.CheckSystemVersion (6, 0))
			{
				if (!GetShouldRotate6 (device.Orientation))
					return;
			}
			else if (!GetShouldRotate (device.Orientation))
				return;

			UIDeviceOrientation? co = this.orientation;
			this.orientation = device.Orientation;

			if (co == null)
				return;

			DisplayPopover (hideFirst: true);
		}

		private bool GetShouldRotate (UIDeviceOrientation orientation)
		{
			UIInterfaceOrientation iorientation = UIInterfaceOrientation.Portrait;
			switch (orientation)
			{
				case UIDeviceOrientation.LandscapeLeft:
					iorientation = UIInterfaceOrientation.LandscapeLeft;
					break;
					
				case UIDeviceOrientation.LandscapeRight:
					iorientation = UIInterfaceOrientation.LandscapeRight;
					break;
					
				case UIDeviceOrientation.Portrait:
					iorientation = UIInterfaceOrientation.Portrait;
					break;
					
				case UIDeviceOrientation.PortraitUpsideDown:
					iorientation = UIInterfaceOrientation.PortraitUpsideDown;
					break;
					
				default: return false;
			}

			return this.viewController.ShouldAutorotateToInterfaceOrientation (iorientation);
		}

		private bool GetShouldRotate6 (UIDeviceOrientation orientation)
		{
			if (!this.viewController.ShouldAutorotate())
				return false;

			UIInterfaceOrientationMask mask = UIInterfaceOrientationMask.Portrait;
			switch (orientation)
			{
				case UIDeviceOrientation.LandscapeLeft:
					mask = UIInterfaceOrientationMask.LandscapeLeft;
					break;
					
				case UIDeviceOrientation.LandscapeRight:
					mask = UIInterfaceOrientationMask.LandscapeRight;
					break;
					
				case UIDeviceOrientation.Portrait:
					mask = UIInterfaceOrientationMask.Portrait;
					break;
					
				case UIDeviceOrientation.PortraitUpsideDown:
					mask = UIInterfaceOrientationMask.PortraitUpsideDown;
					break;
					
				default: return false; 
			}

			return this.viewController.GetSupportedInterfaceOrientations().HasFlag (mask);
		}

		private async Task<MediaFile> GetPictureMediaFileAsync (NSDictionary info)
		{
			var image = (UIImage)info[UIImagePickerController.EditedImage];
			if (image == null)
				image = (UIImage)info[UIImagePickerController.OriginalImage];

            var library = new ALAssetsLibrary();
            var assetUrlTCS = new TaskCompletionSource<NSUrl>();
            if (this.source == UIImagePickerControllerSourceType.Camera)
            {
                // user took a picture
                // get the metadata
                var metadata = info[UIImagePickerController.MediaMetadata] as NSDictionary;
                var newMetadata = new NSMutableDictionary(metadata);
                if (!newMetadata.ContainsKey(ImageIO.CGImageProperties.GPSDictionary))
                {
                    var gpsData = await BuildGPSDataAsync();
                    if (gpsData != null)
                        newMetadata.Add(ImageIO.CGImageProperties.GPSDictionary, gpsData);
                }

                // save to camera roll with metadata
                library.WriteImageToSavedPhotosAlbum(image.CGImage, newMetadata, (newAssetUrl, error) =>
                    {
                        // any additional processing can go here
                        if (error == null)
                            assetUrlTCS.SetResult(newAssetUrl);
                        else
                            assetUrlTCS.SetException(new Exception(error.LocalizedFailureReason));
                    });
            }
            else
            {
                // get the assetUrl for the selected image
                assetUrlTCS.SetResult(info[UIImagePickerController.ReferenceUrl] as NSUrl);
            }

		    if (UIDevice.CurrentDevice.CheckSystemVersion(8, 0))
		    {
                var assetUrl = await assetUrlTCS.Task.ConfigureAwait(false);
                var assets = PHAsset.FetchAssets(new NSUrl[] { assetUrl }, null);
		        if (assets.Count == 0)
		            throw new NullReferenceException("Unable to find the specified asset.");
		        var asset = (PHAsset)assets[0];
                var imgMgr = new PHImageManager();

                var imgTCS = new TaskCompletionSource<NSData>();
		        var imageName = "Unknown";
		        imgMgr.RequestImageData(asset, null, (data, uti, imageOrientation, dictionary) =>
		        {
		            var fileUrl = dictionary["PHImageFileURLKey"].ToString();
		            if (!string.IsNullOrWhiteSpace(fileUrl))
		            {
		                var slash = fileUrl.LastIndexOf('/');
		                if (slash > -1)
		                    imageName = fileUrl.Substring(slash + 1);
		            }
		            imgTCS.SetResult(data);
		        });

		        var img = await imgTCS.Task.ConfigureAwait(false);
		        return new MediaFile(imageName, assetUrl.ToString(), () => img.AsStream(), true);
		    }
		    else
		    {
                // get the default representation of the asset
                var dRepTCS = new TaskCompletionSource<ALAssetRepresentation>();
                var assetUrl = await assetUrlTCS.Task.ConfigureAwait(false);
                library.AssetForUrl(
                    assetUrl,
                    (asset) => dRepTCS.SetResult(asset.DefaultRepresentation),
                    (error) => dRepTCS.SetException(new Exception(error.LocalizedFailureReason))
                );
                var rep = await dRepTCS.Task.ConfigureAwait(false);

                // now some really ugly code to copy that as a byte array
                var size = (uint)rep.Size;
                //byte[] imgData = new byte[size];
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                NSError bError;
                rep.GetBytes(buffer, 0, (uint)size, out bError);
                //Marshal.Copy(buffer, imgData, 0, imgData.Length);
                var imgData = NSData.FromBytes(buffer, (uint)size);
                Marshal.FreeHGlobal(buffer);

                return new MediaFile(rep.Filename, assetUrl.ToString(), imgData.AsStream);
		    }

//			string path = GetOutputPath (MediaImplementation.TypeImage,
//				options.Directory ?? ((IsCaptured) ? String.Empty : "temp"),
//				options.Name);
//            
//            var jpegImage = image.AsJPEG();
//            metadata = await metaDataTCS.Task.ConfigureAwait(false);
//            if (metadata != null && metadata.Count > 0)
//                // getting an error here - looks like some of the metadata is not valid for a jpeg
//                jpegImage.SetValuesForKeysWithDictionary(metadata);

//			using (FileStream fs = File.OpenWrite (path))
//            using (Stream s = new NSDataStream (jpegImage))
//			{
//				s.CopyTo (fs);
//				fs.Flush();
//			}
//
//			Action<bool> dispose = null;
//			if (this.source != UIImagePickerControllerSourceType.Camera)
//				dispose = d => File.Delete (path);
//
//			return new MediaFile (path, () => File.OpenRead (path), dispose: dispose);
		}

        CLLocationManager _locationManager;
        TaskCompletionSource<CLLocation> _locationTCS;
        private async Task<NSDictionary> BuildGPSDataAsync()
        {
            // setup the location manager
            if (_locationManager == null)
            {
                _locationManager = new CLLocationManager();
                if (UIDevice.CurrentDevice.CheckSystemVersion(8, 0))
                    _locationManager.RequestWhenInUseAuthorization();
                _locationManager.DesiredAccuracy = 1; // in meters
            }

            // setup a task for getting the current location and a callback for receiving the location
            _locationTCS = new TaskCompletionSource<CLLocation>();
            _locationManager.LocationsUpdated += (sender, locationArgs) =>
                {
                    if (locationArgs.Locations.Length > 0)
                    {
                        _locationManager.StopUpdatingLocation();
                        _locationTCS.SetResult(locationArgs.Locations[locationArgs.Locations.Length - 1]);
                    }
                };
            // start location monitoring
            _locationManager.StartUpdatingLocation();

            // create a timeout and location task to ensure we don't wait forever
            var timeoutTask = System.Threading.Tasks.Task.Delay(5000); // 5 second wait
            var locationTask = _locationTCS.Task;

            // try and set a location based on whatever task ends first
            CLLocation location;
            var completeTask = await System.Threading.Tasks.Task.WhenAny(locationTask, timeoutTask);
            if (completeTask == locationTask && completeTask.Status == TaskStatus.RanToCompletion)
            {
                // use the location result
                location = locationTask.Result;
            }
            else
            {
                // timeout - stop the location manager and try and use the last location
                _locationManager.StopUpdatingLocation();
                location = _locationManager.Location;
            }

            if (location == null)
                return null;
            
            var gpsData = new NSDictionary(
                ImageIO.CGImageProperties.GPSLatitude, Math.Abs(location.Coordinate.Latitude),
                ImageIO.CGImageProperties.GPSLatitudeRef, (location.Coordinate.Latitude >= 0) ? "N" : "S",
                ImageIO.CGImageProperties.GPSLongitude, Math.Abs(location.Coordinate.Longitude),
                ImageIO.CGImageProperties.GPSLongitudeRef, (location.Coordinate.Longitude >= 0) ? "E" : "W",
                ImageIO.CGImageProperties.GPSAltitude, Math.Abs(location.Altitude),
                ImageIO.CGImageProperties.GPSDateStamp, DateTime.UtcNow.ToString("yyyy:MM:dd"),
                ImageIO.CGImageProperties.GPSTimeStamp, DateTime.UtcNow.ToString("HH:mm:ss.ff")
            );
            return gpsData;
        }

		private MediaFile GetMovieMediaFile (NSDictionary info)
		{
			NSUrl url = (NSUrl)info[UIImagePickerController.MediaURL];

            string name = this.options.Name ?? Path.GetFileName(url.Path);
          string path = GetOutputPath(MediaImplementation.TypeMovie,
    				options.Directory ?? ((IsCaptured) ? String.Empty : "temp"),
    				this.options.Name ?? Path.GetFileName (url.Path));

			File.Move (url.Path, path);

			Action<bool> dispose = null;
			if (this.source != UIImagePickerControllerSourceType.Camera)
				dispose = d => File.Delete (path);

			return new MediaFile (name, path, () => File.OpenRead (path), dispose: dispose);
		}
		
		private static string GetUniquePath (string type, string path, string name)
		{
      bool isPhoto = (type == MediaImplementation.TypeImage);
			string ext = Path.GetExtension (name);
			if (ext == String.Empty)
				ext = ((isPhoto) ? ".jpg" : ".mp4");

			name = Path.GetFileNameWithoutExtension (name);

			string nname = name + ext;
			int i = 1;
			while (File.Exists (Path.Combine (path, nname)))
				nname = name + "_" + (i++) + ext;

			return Path.Combine (path, nname);
		}

		private static string GetOutputPath (string type, string path, string name)
		{
			path = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.Personal), path);
			Directory.CreateDirectory (path);

			if (String.IsNullOrWhiteSpace (name))
			{
				string timestamp = DateTime.Now.ToString ("yyyMMdd_HHmmss");
        if (type == MediaImplementation.TypeImage)
					name = "IMG_" + timestamp + ".jpg";
				else
					name = "VID_" + timestamp + ".mp4";
			}

			return Path.Combine (path, GetUniquePath (type, path, name));
		}
		
		private static bool IsValidInterfaceOrientation (UIDeviceOrientation self)
		{
			return (self != UIDeviceOrientation.FaceUp && self != UIDeviceOrientation.FaceDown && self != UIDeviceOrientation.Unknown);
		}
		
		private static bool IsSameOrientationKind (UIDeviceOrientation o1, UIDeviceOrientation o2)
		{
			if (o1 == UIDeviceOrientation.FaceDown || o1 == UIDeviceOrientation.FaceUp)
				return (o2 == UIDeviceOrientation.FaceDown || o2 == UIDeviceOrientation.FaceUp);
			if (o1 == UIDeviceOrientation.LandscapeLeft || o1 == UIDeviceOrientation.LandscapeRight)
				return (o2 == UIDeviceOrientation.LandscapeLeft || o2 == UIDeviceOrientation.LandscapeRight);
			if (o1 == UIDeviceOrientation.Portrait || o1 == UIDeviceOrientation.PortraitUpsideDown)
				return (o2 == UIDeviceOrientation.Portrait || o2 == UIDeviceOrientation.PortraitUpsideDown);
			
			return false;
		}
		
		private static UIDeviceOrientation GetDeviceOrientation (UIInterfaceOrientation self)
		{
			switch (self)
			{
				case UIInterfaceOrientation.LandscapeLeft:
					return UIDeviceOrientation.LandscapeLeft;
				case UIInterfaceOrientation.LandscapeRight:
					return UIDeviceOrientation.LandscapeRight;
				case UIInterfaceOrientation.Portrait:
					return UIDeviceOrientation.Portrait;
				case UIInterfaceOrientation.PortraitUpsideDown:
					return UIDeviceOrientation.PortraitUpsideDown;
				default:
					throw new InvalidOperationException();
			}
		}
	}
}
