using System;
using MonoTouch.CoreLocation;
using System.Threading.Tasks;
using System.Threading;
using MonoTouch.Foundation;

namespace Xamarin.Geolocation
{
	public class Geolocator
	{
		public Geolocator()
		{
			this.manager = GetManager();
			this.manager.AuthorizationChanged += OnAuthorizationChanged;
			this.manager.Failed += OnFailed;
			this.manager.UpdatedLocation += OnUpdatedLocation;
			this.manager.UpdatedHeading += OnUpdatedHeading;
		}

		public event EventHandler<PositionErrorEventArgs> PositionError;

		public event EventHandler<PositionEventArgs> PositionChanged;

		public double DesiredAccuracy
		{
			get;
			set;
		}

		public bool IsListening
		{
			get { return this.isListening; }
		}

		public bool SupportsHeading
		{
			get { return CLLocationManager.HeadingAvailable; }
		}

		public bool IsGeolocationAvailable
		{
			get { return true; } // all iOS devices support at least wifi geolocation
		}

		public bool IsGeolocationEnabled
		{
			get { return CLLocationManager.LocationServicesEnabled; }
		}

		public Task<Position> GetPositionAsync (int timeout)
		{
			return GetPositionAsync (timeout, CancellationToken.None);
		}

		public Task<Position> GetPositionAsync (CancellationToken cancelToken)
		{
			return GetPositionAsync (Timeout.Infinite, cancelToken);
		}

		public Task<Position> GetPositionAsync (int timeout, CancellationToken cancelToken)
		{
			if (timeout <= 0 && timeout != Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("timeout", "Timeout must be positive or Timeout.Infinite");

			TaskCompletionSource<Position> tcs;
			if (!IsListening)
			{
				var m = GetManager();

				tcs = new TaskCompletionSource<Position> (m);
				var singleListener = new GeolocationSingleUpdateDelegate (m, DesiredAccuracy, timeout, cancelToken);
				m.Delegate = singleListener;

				m.StartUpdatingLocation ();
				if (SupportsHeading)
					m.StartUpdatingHeading ();

				return singleListener.Task;
			}
			else
			{
				tcs = new TaskCompletionSource<Position>();
				if (this.position == null)
				{
					EventHandler<PositionErrorEventArgs> gotError = null;
					gotError = (s,e) =>
					{
						tcs.TrySetException (new GeolocationException (e.Error));
						PositionError -= gotError;
					};
					
					PositionError += gotError;
					
					EventHandler<PositionEventArgs> gotPosition = null;
					gotPosition = (s, e) =>
					{
						tcs.TrySetResult (e.Position);
						PositionChanged -= gotPosition;
					};

					PositionChanged += gotPosition;
				}
				else
					tcs.SetResult (this.position);
			}

			return tcs.Task;
		}

		public void StartListening (int minTime, double minDistance)
		{
			if (minTime < 0)
				throw new ArgumentOutOfRangeException ("minTime");
			if (minDistance < 0)
				throw new ArgumentOutOfRangeException ("minDistance");
			if (this.isListening)
				throw new InvalidOperationException ("Already listening");

			this.isListening = true;
			this.manager.DesiredAccuracy = DesiredAccuracy;
			this.manager.DistanceFilter = minDistance;
			this.manager.StartUpdatingLocation ();

			if (CLLocationManager.HeadingAvailable)
				this.manager.StartUpdatingHeading ();
		}

		public void StopListening ()
		{
			if (!this.isListening)
				return;

			this.isListening = false;
			if (CLLocationManager.HeadingAvailable)
				this.manager.StopUpdatingHeading ();

			this.manager.StopUpdatingLocation ();
			this.position = null;
		}

		private readonly CLLocationManager manager;
		private bool isListening;
		private Position position;

		private CLLocationManager GetManager()
		{
			CLLocationManager m = null;
			new NSObject().InvokeOnMainThread (() => m = new CLLocationManager());
			return m;
		}
		
		private void OnUpdatedHeading (object sender, CLHeadingUpdatedEventArgs e)
		{
			if (e.NewHeading.TrueHeading == -1)
				return;

			Position p = (this.position == null) ? new Position () : new Position (this.position);

			p.Heading = e.NewHeading.TrueHeading;

			this.position = p;
			
			OnPositionChanged (new PositionEventArgs (p));
		}

		private void OnUpdatedLocation (object sender, CLLocationUpdatedEventArgs e)
		{
			Position p = (this.position == null) ? new Position () : new Position (this.position);
			
			if (e.NewLocation.HorizontalAccuracy > -1)
			{
				p.Accuracy = e.NewLocation.HorizontalAccuracy;
				p.Latitude = e.NewLocation.Coordinate.Latitude;
				p.Longitude = e.NewLocation.Coordinate.Longitude;
			}
			
			if (e.NewLocation.VerticalAccuracy > -1)
			{
				p.Altitude = e.NewLocation.Altitude;
				p.AltitudeAccuracy = e.NewLocation.VerticalAccuracy;
			}
			
			if (e.NewLocation.Speed > -1)
				p.Speed = e.NewLocation.Speed;
			
			p.Timestamp = new DateTimeOffset (e.NewLocation.Timestamp);

			this.position = p;

			OnPositionChanged (new PositionEventArgs (p));
		}
		
		private void OnFailed (object sender, MonoTouch.Foundation.NSErrorEventArgs e)
		{
			if ((CLError)e.Error.Code == CLError.Network)
				OnPositionError (new PositionErrorEventArgs (GeolocationError.PositionUnavailable));
		}

		private void OnAuthorizationChanged (object sender, CLAuthroziationChangedEventArgs e)
		{
			if (e.Status == CLAuthorizationStatus.Denied || e.Status == CLAuthorizationStatus.Restricted)
				OnPositionError (new PositionErrorEventArgs (GeolocationError.Unauthorized));
		}
		
		private void OnPositionChanged (PositionEventArgs e)
		{
			var changed = PositionChanged;
			if (changed != null)
				changed (this, e);
		}
		
		private void OnPositionError (PositionErrorEventArgs e)
		{
			StopListening();
			
			var error = PositionError;
			if (error != null)
				error (this, e);
		}
	}
}