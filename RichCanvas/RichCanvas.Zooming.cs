using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using RichCanvas.Gestures;

namespace RichCanvas
{
    public partial class RichCanvas
    {
        #region Dependency Properties

        /// <summary>
        /// Identifies the <see cref="ScaleFactor"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ScaleFactorProperty = DependencyProperty.Register(nameof(ScaleFactor), typeof(double), typeof(RichCanvas), new FrameworkPropertyMetadata(1.1d, null, CoerceScaleFactor));

        /// <summary>
        /// Gets or sets the factor used to change <see cref="ScaleTransform"/> on zoom.
        /// Default is 1.1d.
        /// </summary>
        public double ScaleFactor
        {
            get => (double)GetValue(ScaleFactorProperty);
            set => SetValue(ScaleFactorProperty, value);
        }

        private static object CoerceScaleFactor(DependencyObject d, object value)
        {
            double scaleFactorValue = (double)value;
            // Security: Validate scale factor is within reasonable bounds
            if (scaleFactorValue <= 0 || double.IsNaN(scaleFactorValue) || double.IsInfinity(scaleFactorValue))
            {
                return 1.1d; // Return default safe value
            }
            // Limit scale factor to reasonable range (0.1 to 10)
            return Math.Max(0.1d, Math.Min(10d, scaleFactorValue));
        }

        /// <summary>
        /// Identifies the <see cref="DisableZoom"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty DisableZoomProperty = DependencyProperty.Register(nameof(DisableZoom), typeof(bool), typeof(RichCanvas), new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Gets or sets whether zooming operation is disabled.
        /// Default is enabled.
        /// </summary>
        public bool DisableZoom
        {
            get => (bool)GetValue(DisableZoomProperty);
            set => SetValue(DisableZoomProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="MaxScale"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty MaxScaleProperty = DependencyProperty.Register(nameof(MaxScale), typeof(double), typeof(RichCanvas), new FrameworkPropertyMetadata(2d, OnMaxScaleChanged, CoerceMaxScale));

        /// <summary>
        /// Gets or sets maximum scale for <see cref="ScaleTransform"/>.
        /// Default is 2.
        /// </summary>
        public double MaxScale
        {
            get => (double)GetValue(MaxScaleProperty);
            set => SetValue(MaxScaleProperty, value);
        }

        private static object CoerceMaxScale(DependencyObject d, object value)
        {
            var zoom = (RichCanvas)d;
            double min = zoom.MinScale;
            double maxValue = (double)value;

            // Security: Validate max scale value
            if (double.IsNaN(maxValue) || double.IsInfinity(maxValue) || maxValue <= 0)
            {
                return 2d; // Return default safe value
            }

            // Ensure max is greater than min
            if (maxValue < min)
            {
                return Math.Max(min, 2d);
            }

            // Limit to reasonable maximum (e.g., 100x zoom)
            return Math.Min(maxValue, 100d);
        }

        private static void OnMaxScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var zoom = (RichCanvas)d;
            zoom.CoerceValue(ViewportZoomProperty);
        }

        /// <summary>
        /// Identifies the <see cref="MinScale"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty MinScaleProperty = DependencyProperty.Register(nameof(MinScale), typeof(double), typeof(RichCanvas), new FrameworkPropertyMetadata(0.1d, OnMinimumScaleChanged, CoerceMinimumScale));

        /// <summary>
        /// Gets or sets minimum scale for <see cref="RichCanvas.ScaleTransform"/>.
        /// Default is 0.1d.
        /// </summary>
        public double MinScale
        {
            get => (double)GetValue(MinScaleProperty);
            set => SetValue(MinScaleProperty, value);
        }

        private static void OnMinimumScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var zoom = (RichCanvas)d;
            zoom.CoerceValue(MaxScaleProperty);
            zoom.CoerceValue(ViewportZoomProperty);
        }

        private static object CoerceMinimumScale(DependencyObject d, object value)
        {
            double minValue = (double)value;
            
            // Security: Validate min scale value
            if (double.IsNaN(minValue) || double.IsInfinity(minValue) || minValue <= 0)
            {
                return 0.1d; // Return default safe value
            }
            
            // Limit to reasonable minimum (e.g., 0.01 = 1% zoom)
            return Math.Max(0.01d, Math.Min(minValue, 1d));
        }

        /// <summary>
        /// Identifies the <see cref="ViewportZoom"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ViewportZoomProperty = DependencyProperty.Register(nameof(ViewportZoom), typeof(double), typeof(RichCanvas), new FrameworkPropertyMetadata(1d, OnViewportZoomChanged, CoerceViewportZoom));

        /// <summary>
        /// Gets or sets the current <see cref="RichCanvas.ScaleTransform"/> value.
        /// Default is 1.
        /// </summary>
        public double ViewportZoom
        {
            get => (double)GetValue(ViewportZoomProperty);
            set => SetValue(ViewportZoomProperty, value);
        }

        private static void OnViewportZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var canvas = (RichCanvas)d;
            double newValue = (double)e.NewValue;
            
            // Security: Additional validation before applying scale
            if (!double.IsNaN(newValue) && !double.IsInfinity(newValue) && newValue > 0)
            {
                canvas.OverrideScale(newValue);
            }
        }

        private static object CoerceViewportZoom(DependencyObject d, object value)
        {
            var itemsControl = (RichCanvas)d;
            double zoomValue = (double)value;

            // Security: Validate zoom value
            if (double.IsNaN(zoomValue) || double.IsInfinity(zoomValue) || zoomValue <= 0)
            {
                return itemsControl.ViewportZoom; // Keep current value if invalid
            }

            if (itemsControl.DisableZoom)
            {
                return itemsControl.ViewportZoom;
            }

            double minimum = itemsControl.MinScale;
            if (zoomValue < minimum)
            {
                return minimum;
            }

            double maximum = itemsControl.MaxScale;
            if (zoomValue > maximum)
            {
                return maximum;
            }

            return zoomValue;
        }

        /// <summary>
        /// Identifies the <see cref="Zooming"/> routed event.
        /// </summary>
        public static readonly RoutedEvent ZoomingEvent = EventManager.RegisterRoutedEvent(nameof(Zooming), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(RichCanvas));

        /// <summary>
        /// Occurs whenever <see cref="RichCanvas"/> is zooomed in or out.
        /// </summary>
        public event RoutedEventHandler Zooming
        {
            add { AddHandler(ZoomingEvent, value); }
            remove { RemoveHandler(ZoomingEvent, value); }
        }

        #endregion Dependency Properties

        /// <inheritdoc/>
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (e == null) return; // Security: Null check

            if (RichCanvasGestures.ZoomModifierKey == Keyboard.Modifiers)
            {
                Point position = e.GetPosition(ItemsHost);
                IsZooming = true;
                
                // Security: Validate delta to prevent division by zero
                double scaleFactor = e.Delta > 0 ? ScaleFactor : 1 / ScaleFactor;
                if (!double.IsNaN(scaleFactor) && !double.IsInfinity(scaleFactor) && scaleFactor > 0)
                {
                    ZoomAtPosition(position, scaleFactor);
                }
                
                IsZooming = false;
                // handle the event so it won't trigger scrolling
                e.Handled = true;
            }
        }

        /// <summary>
        /// Zooms the <see cref="RichCanvas"/> at the specified <paramref name="mousePosition"/> using the given <paramref name="delta"/>.
        /// </summary>
        /// <param name="mousePosition">Mouse position where to zoom at.</param>
        /// <param name="delta">Value of each zooming step.</param>
        public void ZoomAtPosition(Point mousePosition, double delta)
        {
            // Security: Validate delta parameter
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0)
            {
                return; // Invalid delta, do nothing
            }

            if (!DisableZoom)
            {
                Point previouslyTransformedMousePosition = AppliedTransform.Transform(mousePosition);

                double previousZoom = ViewportZoom;
                double newZoom = ViewportZoom * delta;
                
                // Security: Validate new zoom value before applying
                if (!double.IsNaN(newZoom) && !double.IsInfinity(newZoom) && newZoom > 0)
                {
                    ViewportZoom = newZoom;

                    if (Math.Abs(previousZoom - ViewportZoom) > 0.001)
                    {
                        Point transformedMousePositionAfterScaling = AppliedTransform.Transform(mousePosition);

                        Vector translationAdjustment = previouslyTransformedMousePosition - transformedMousePositionAfterScaling;
                        Point newTranslation = new Point(TranslateTransform.X, TranslateTransform.Y) + translationAdjustment;

                        Vector viewportLocation = (new Vector(0, 0) - (Vector)newTranslation) / ViewportZoom;

                        ViewportLocation = (Point)viewportLocation;
                    }
                }
            }
        }

        /// <summary>
        /// Zooms in the <see cref="RichCanvas"/> using its <see cref="MousePosition"/> and its <see cref="ScaleFactor"/>.
        /// </summary>
        public void ZoomIn() => ZoomAtPosition(MousePosition, ScaleFactor);

        /// <summary>
        /// Zooms out the <see cref="RichCanvas"/> using its <see cref="MousePosition"/> and its <see cref="ScaleFactor"/>.
        /// </summary>
        public void ZoomOut() => ZoomAtPosition(MousePosition, 1 / ScaleFactor);

        private void OverrideScale(double zoom)
        {
            // Security: Final validation before applying scale
            if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0)
            {
                return; // Invalid zoom value, do nothing
            }

            ScaleTransform.ScaleX = zoom;
            ScaleTransform.ScaleY = zoom;
            var zoomEventArgs = new RoutedEventArgs(ZoomingEvent, new Point(ScaleTransform.ScaleX, ScaleTransform.ScaleY));
            RaiseEvent(zoomEventArgs);

            // Security: Prevent division by zero
            if (ViewportZoom > 0)
            {
                ViewportSize = new Size(ActualWidth / ViewportZoom, ActualHeight / ViewportZoom);
            }

            UpdateScrollbars();
        }
    }
}