using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RichCanvas
{
    public partial class RichCanvas
    {
        #region Fixed Dependency Properties with Security Enhancements

        /// <summary>
        /// Identifies the <see cref="MousePosition"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty MousePositionProperty = DependencyProperty.Register(
            nameof(MousePosition), 
            typeof(Point), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(Point), null, CoerceMousePosition));

        private static object CoerceMousePosition(DependencyObject d, object value)
        {
            if (value is Point point)
            {
                // Security: Validate point is within reasonable bounds
                if (double.IsNaN(point.X) || double.IsNaN(point.Y) ||
                    double.IsInfinity(point.X) || double.IsInfinity(point.Y))
                {
                    return new Point(0, 0); // Return safe default
                }
                return point;
            }
            return new Point(0, 0);
        }

        /// <summary>
        /// Identifies the <see cref="EnableAutoPanning"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty EnableAutoPanningProperty = DependencyProperty.Register(
            nameof(EnableAutoPanning), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false, OnEnableAutoPanningChanged));

        /// <summary>
        /// Identifies the <see cref="AutoPanTickRate"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty AutoPanTickRateProperty = DependencyProperty.Register(
            nameof(AutoPanTickRate), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(1f, OnAutoPanTickRateChanged, CoerceAutoPanTickRate));

        private static object CoerceAutoPanTickRate(DependencyObject d, object value)
        {
            float rate = (float)value;
            // Security: Validate tick rate is within reasonable bounds (0.1 to 1000 ms)
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0)
            {
                return 1f; // Return default safe value
            }
            return Math.Max(0.1f, Math.Min(1000f, rate));
        }

        /// <summary>
        /// Identifies the <see cref="AutoPanSpeed"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty AutoPanSpeedProperty = DependencyProperty.Register(
            nameof(AutoPanSpeed), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(1f, null, CoerceAutoPanSpeed));

        private static object CoerceAutoPanSpeed(DependencyObject d, object value)
        {
            float speed = (float)value;
            // Security: Validate speed is within reasonable bounds
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0)
            {
                return 1f; // Return default safe value
            }
            return Math.Max(0.1f, Math.Min(100f, speed));
        }

        /// <summary>
        /// Identifies the <see cref="GridSpacing"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
            nameof(GridSpacing), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(20f, null, CoerceGridSpacing));

        private static object CoerceGridSpacing(DependencyObject d, object value)
        {
            float spacing = (float)value;
            // Security: Validate grid spacing is within reasonable bounds
            if (float.IsNaN(spacing) || float.IsInfinity(spacing) || spacing <= 0)
            {
                return 20f; // Return default safe value
            }
            return Math.Max(1f, Math.Min(1000f, spacing));
        }

        /// <summary>
        /// Identifies the <see cref="ViewportLocation"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ViewportLocationProperty = DependencyProperty.Register(
            nameof(ViewportLocation), 
            typeof(Point), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(Point), OnViewportLocationChanged, CoerceViewportLocation));

        private static object CoerceViewportLocation(DependencyObject d, object value)
        {
            if (value is Point point)
            {
                // Security: Validate viewport location
                if (double.IsNaN(point.X) || double.IsNaN(point.Y) ||
                    double.IsInfinity(point.X) || double.IsInfinity(point.Y))
                {
                    return new Point(0, 0); // Return safe default
                }
                // Limit to reasonable viewport bounds
                double x = Math.Max(-100000, Math.Min(100000, point.X));
                double y = Math.Max(-100000, Math.Min(100000, point.Y));
                return new Point(x, y);
            }
            return new Point(0, 0);
        }

        /// <summary>
        /// Identifies the <see cref="EnableSnapping"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty EnableSnappingProperty = DependencyProperty.Register(
            nameof(EnableSnapping), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Identifies the <see cref="SelectionRectangleStyle"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty SelectionRectangleStyleProperty = DependencyProperty.Register(
            nameof(SelectionRectangleStyle), 
            typeof(Style), 
            typeof(RichCanvas));

        /// <summary>
        /// Identifies the <see cref="ScrollFactor"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ScrollFactorProperty = DependencyProperty.Register(
            nameof(ScrollFactor), 
            typeof(double), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(10d, null, CoerceScrollFactor));

        private static object CoerceScrollFactor(DependencyObject d, object value)
        {
            double factor = (double)value;
            // Security: Validate scroll factor
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
            {
                return 10d; // Return default safe value
            }
            return Math.Max(1d, Math.Min(100d, factor));
        }

        /// <summary>
        /// Identifies the <see cref="SelectedItems"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
            nameof(SelectedItems), 
            typeof(IList), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(IList), OnSelectedItemsSourceChanged));

        /// <summary>
        /// Identifies the <see cref="DisableCache"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty DisableCacheProperty = DependencyProperty.Register(
            nameof(DisableCache), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(true, OnDisableCacheChanged));

        /// <summary>
        /// Identifies the <see cref="RealTimeSelectionEnabled"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty RealTimeSelectionEnabledProperty = DependencyProperty.Register(
            nameof(RealTimeSelectionEnabled), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Identifies the <see cref="RealTimeDraggingEnabled"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty RealTimeDraggingEnabledProperty = DependencyProperty.Register(
            nameof(RealTimeDraggingEnabled), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Identifies the <see cref="CanSelectMultipleItems"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty CanSelectMultipleItemsProperty = DependencyProperty.Register(
            nameof(CanSelectMultipleItems), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(true, OnCanSelectMultipleItemsChanged));

        #endregion
    }
}