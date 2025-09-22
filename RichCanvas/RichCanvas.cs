using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using RichCanvas.Automation;
using RichCanvas.CustomEventArgs;
using RichCanvas.Helpers;
using RichCanvas.States;

namespace RichCanvas
{
    /// <summary>
    /// ItemsControl hosting <see cref="RichCanvasPanel"/>
    /// </summary>
    [TemplatePart(Name = DrawingPanelName, Type = typeof(Panel))]
    [TemplatePart(Name = SelectionRectangleName, Type = typeof(Rectangle))]
    [StyleTypedProperty(Property = nameof(SelectionRectangleStyle), StyleTargetType = typeof(Rectangle))]
    public partial class RichCanvas : MultiSelector
    {
        #region Constants

        private const string DrawingPanelName = "PART_Panel";
        private const string SelectionRectangleName = "PART_SelectionRectangle";
        
        // Security: Define safe bounds for numeric properties
        private const float MinAutoTickRate = 0.1f;
        private const float MaxAutoTickRate = 1000f;
        private const float MinAutoSpeed = 0.1f;
        private const float MaxAutoSpeed = 100f;
        private const float MinGridSpacing = 1f;
        private const float MaxGridSpacing = 1000f;
        private const double MinScrollFactor = 1d;
        private const double MaxScrollFactor = 100d;

        #endregion Constants

        #region Private Fields

        internal readonly ScaleTransform ScaleTransform = new ScaleTransform();
        internal readonly TranslateTransform TranslateTransform = new TranslateTransform();
        private RichCanvasPanel _mainPanel;
        private DispatcherTimer _autoPanTimer;
        private Stack<CanvasState> _states;

        #endregion Private Fields

        #region Properties API

        /// <summary>
        /// Gets the current state telling the action that happens on <see cref="RichCanvas"/>.
        /// </summary>
        public CanvasState CurrentState => _states?.Count > 0 ? _states.Peek() : null;

        /// <summary>
        /// Identifies the <see cref="MousePosition"/> dependency property.
        /// </summary>
        public static DependencyProperty MousePositionProperty = DependencyProperty.Register(
            nameof(MousePosition), 
            typeof(Point), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(Point), null, CoerceMousePosition));

        // Security: Validate mouse position to prevent extreme values
        private static object CoerceMousePosition(DependencyObject d, object value)
        {
            if (value is Point point)
            {
                // Prevent NaN and infinite values
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Y) || double.IsInfinity(point.Y))
                {
                    return new Point(0, 0);
                }
                
                // Clamp to reasonable bounds
                double x = Math.Max(-100000, Math.Min(100000, point.X));
                double y = Math.Max(-100000, Math.Min(100000, point.Y));
                return new Point(x, y);
            }
            return new Point(0, 0);
        }

        /// <summary>
        /// Gets or sets mouse position relative to <see cref="ItemsHost"/>.
        /// </summary>
        public Point MousePosition
        {
            get => (Point)GetValue(MousePositionProperty);
            set => SetValue(MousePositionProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="SelectionRectangle"/> dependency property key.
        /// </summary>
        protected static readonly DependencyPropertyKey SelectionRectanglePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectionRectangle), 
            typeof(Rect), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(Rect)));

        /// <summary>
        /// Identifies the read-only <see cref="SelectionRectangle"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty SelectionRectangleProperty = SelectionRectanglePropertyKey.DependencyProperty;

        /// <summary>
        /// Gets the selection area as <see cref="Rect"/>.
        /// </summary>
        public Rect SelectionRectangle
        {
            get => (Rect)GetValue(SelectionRectangleProperty);
            internal set 
            {
                // Security: Validate rectangle bounds
                if (!double.IsNaN(value.Width) && !double.IsInfinity(value.Width) &&
                    !double.IsNaN(value.Height) && !double.IsInfinity(value.Height) &&
                    !double.IsNaN(value.X) && !double.IsInfinity(value.X) &&
                    !double.IsNaN(value.Y) && !double.IsInfinity(value.Y) &&
                    value.Width >= 0 && value.Height >= 0)
                {
                    SetValue(SelectionRectanglePropertyKey, value);
                }
            }
        }

        /// <summary>
        /// Identifies the <see cref="IsSelecting"/> dependency property key.
        /// </summary>
        protected static readonly DependencyPropertyKey IsSelectingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsSelecting), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Identifies the read-only <see cref="IsSelecting"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty IsSelectingProperty = IsSelectingPropertyKey.DependencyProperty;

        /// <summary>
        /// Gets whether the operation in progress is selection.
        /// </summary>
        public bool IsSelecting
        {
            get => (bool)GetValue(IsSelectingProperty);
            internal set => SetValue(IsSelectingPropertyKey, value);
        }

        /// <summary>
        /// Identifies the <see cref="AppliedTransform"/> dependency property key.
        /// </summary>
        protected static readonly DependencyPropertyKey AppliedTransformPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AppliedTransform), 
            typeof(TransformGroup), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(TransformGroup)));

        /// <summary>
        /// Identifies the read-only <see cref="AppliedTransform"/> dependency property.
        /// </summary>
        public static DependencyProperty AppliedTransformProperty = AppliedTransformPropertyKey.DependencyProperty;

        /// <summary>
        /// Gets the transform that is applied to all child controls.
        /// </summary>
        public TransformGroup AppliedTransform
        {
            get => (TransformGroup)GetValue(AppliedTransformProperty);
            internal set => SetValue(AppliedTransformPropertyKey, value);
        }

        /// <summary>
        /// Identifies the <see cref="EnableAutoPanning"/> dependency property.
        /// </summary>
        public static DependencyProperty EnableAutoPanningProperty = DependencyProperty.Register(
            nameof(EnableAutoPanning), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false, OnEnableAutoPanningChanged));

        /// <summary>
        /// Gets or sets whether Auto-Panning is enabled.
        /// Default is disabled.
        /// </summary>
        public bool EnableAutoPanning
        {
            get => (bool)GetValue(EnableAutoPanningProperty);
            set => SetValue(EnableAutoPanningProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="AutoPanTickRate"/> dependency property.
        /// </summary>
        public static DependencyProperty AutoPanTickRateProperty = DependencyProperty.Register(
            nameof(AutoPanTickRate), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(1f, OnAutoPanTickRateChanged, CoerceAutoPanTickRate));

        // Security: Validate auto-pan tick rate to prevent DoS
        private static object CoerceAutoPanTickRate(DependencyObject d, object value)
        {
            if (value is float floatValue)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    return 1f;
                    
                return Math.Max(MinAutoTickRate, Math.Min(MaxAutoTickRate, floatValue));
            }
            return 1f;
        }

        /// <summary>
        /// Gets or sets <see cref="DispatcherTimer"/> interval value.
        /// Default is 1.
        /// </summary>
        public float AutoPanTickRate
        {
            get => (float)GetValue(AutoPanTickRateProperty);
            set => SetValue(AutoPanTickRateProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="AutoPanSpeed"/> dependency property.
        /// </summary>
        public static DependencyProperty AutoPanSpeedProperty = DependencyProperty.Register(
            nameof(AutoPanSpeed), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(1f, null, CoerceAutoPanSpeed));

        // Security: Validate auto-pan speed to prevent extreme movements
        private static object CoerceAutoPanSpeed(DependencyObject d, object value)
        {
            if (value is float floatValue)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    return 1f;
                    
                return Math.Max(MinAutoSpeed, Math.Min(MaxAutoSpeed, floatValue));
            }
            return 1f;
        }

        /// <summary>
        /// Gets or sets the <see cref="ItemsHost"/> translate speed.
        /// Default is 1.
        /// </summary>
        public float AutoPanSpeed
        {
            get => (float)GetValue(AutoPanSpeedProperty);
            set => SetValue(AutoPanSpeedProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="GridSpacing"/> dependency property.
        /// </summary>
        public static DependencyProperty GridSpacingProperty = DependencyProperty.Register(
            nameof(GridSpacing), 
            typeof(float), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(20f, null, CoerceGridSpacing));

        // Security: Validate grid spacing to prevent extreme values
        private static object CoerceGridSpacing(DependencyObject d, object value)
        {
            if (value is float floatValue)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    return 20f;
                    
                return Math.Max(MinGridSpacing, Math.Min(MaxGridSpacing, floatValue));
            }
            return 20f;
        }

        /// <summary>
        /// Gets or sets grid drawing viewport size.
        /// Default is 20.
        /// </summary>
        public float GridSpacing
        {
            get => (float)GetValue(GridSpacingProperty);
            set => SetValue(GridSpacingProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ViewportLocation"/> dependency property.
        /// </summary>
        public static DependencyProperty ViewportLocationProperty = DependencyProperty.Register(
            nameof(ViewportLocation), 
            typeof(Point), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(Point), OnViewportLocationChanged, CoerceViewportLocation));

        // Security: Validate viewport location to prevent extreme coordinates
        private static object CoerceViewportLocation(DependencyObject d, object value)
        {
            if (value is Point point)
            {
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Y) || double.IsInfinity(point.Y))
                {
                    return new Point(0, 0);
                }
                
                // Clamp to reasonable viewport bounds
                double x = Math.Max(-50000, Math.Min(50000, point.X));
                double y = Math.Max(-50000, Math.Min(50000, point.Y));
                return new Point(x, y);
            }
            return new Point(0, 0);
        }

        /// <summary>
        /// Gets current viewport rectangle.
        /// </summary>
        public Point ViewportLocation
        {
            get => (Point)GetValue(ViewportLocationProperty);
            set => SetValue(ViewportLocationProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="EnableSnapping"/> dependency property.
        /// </summary>
        public static DependencyProperty EnableSnappingProperty = DependencyProperty.Register(
            nameof(EnableSnapping), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Gets or sets whether grid snap correction on <see cref="RichCanvasContainer"/> is applied.
        /// Default is disabled.
        /// </summary>
        public bool EnableSnapping
        {
            get => (bool)GetValue(EnableSnappingProperty);
            set => SetValue(EnableSnappingProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="SelectionRectangleStyle"/> dependency property.
        /// </summary>
        public static DependencyProperty SelectionRectangleStyleProperty = DependencyProperty.Register(
            nameof(SelectionRectangleStyle), 
            typeof(Style), 
            typeof(RichCanvas),
            new FrameworkPropertyMetadata(null, OnSelectionRectangleStyleChanged));

        // Security: Validate style to prevent malicious styles
        private static void OnSelectionRectangleStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null && !(e.NewValue is Style style && style.TargetType == typeof(Rectangle)))
            {
                // Reset to null if invalid style
                d.SetValue(SelectionRectangleStyleProperty, null);
            }
        }

        /// <summary>
        /// Gets or sets selection <see cref="Rectangle"/> style.
        /// </summary>
        public Style SelectionRectangleStyle
        {
            get => (Style)GetValue(SelectionRectangleStyleProperty);
            set => SetValue(SelectionRectangleStyleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ScrollFactor"/> dependency property.
        /// </summary>
        public static DependencyProperty ScrollFactorProperty = DependencyProperty.Register(
            nameof(ScrollFactor), 
            typeof(double), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(10d, null, CoerceScrollFactor));

        // Security: Enhanced scroll factor validation
        private static object CoerceScrollFactor(DependencyObject d, object value)
        {
            if (value is double doubleValue)
            {
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue) || doubleValue <= 0)
                    return 10d;
                    
                return Math.Max(MinScrollFactor, Math.Min(MaxScrollFactor, doubleValue));
            }
            return 10d;
        }

        /// <summary>
        /// Gets or sets the scrolling factor applied when scrolling.
        /// Default is 10.
        /// </summary>
        public double ScrollFactor
        {
            get => (double)GetValue(ScrollFactorProperty);
            set => SetValue(ScrollFactorProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="SelectedItems"/> dependency property.
        /// </summary>
        public static DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
            nameof(SelectedItems), 
            typeof(IList), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(default(IList), OnSelectedItemsSourceChanged, CoerceSelectedItems));

        // Security: Validate selected items collection
        private static object CoerceSelectedItems(DependencyObject d, object value)
        {
            // Prevent null collections which could cause NullReferenceException
            if (value == null)
            {
                return new List<object>();
            }
            
            // Ensure we have a valid collection type
            if (!(value is IList))
            {
                return new List<object>();
            }
            
            return value;
        }

        /// <summary>
        /// Gets or sets the items in the <see cref="RichCanvas"/> that are selected.
        /// </summary>
        public new IList SelectedItems
        {
            get => (IList)GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="DrawingEnded"/> routed event.
        /// </summary>
        public static readonly RoutedEvent DrawingEndedEvent = EventManager.RegisterRoutedEvent(
            nameof(DrawingEnded), 
            RoutingStrategy.Bubble, 
            typeof(RoutedEventHandler), 
            typeof(RichCanvas));

        /// <summary>
        /// Occurs whenever <see cref="OnMouseUp(MouseButtonEventArgs)"/> is called after drawing operation is finished.
        /// </summary>
        public event RoutedEventHandler DrawingEnded
        {
            add { AddHandler(DrawingEndedEvent, value); }
            remove { RemoveHandler(DrawingEndedEvent, value); }
        }

        /// <summary>
        /// Identifies the <see cref="DrawingEndedCommand"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty DrawingEndedCommandProperty = DependencyProperty.Register(
            nameof(DrawingEndedCommand), 
            typeof(ICommand), 
            typeof(RichCanvas));

        /// <summary>
        /// Invoked when drawing operation is completed. <br />
        /// Parameter is <see cref="Point"/>, representing the mouse position when drawing has finished.
        /// </summary>
        public ICommand DrawingEndedCommand
        {
            get => (ICommand)GetValue(DrawingEndedCommandProperty);
            set => SetValue(DrawingEndedCommandProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="DisableCache"/> dependency property.
        /// </summary>
        public static DependencyProperty DisableCacheProperty = DependencyProperty.Register(
            nameof(DisableCache), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(true, OnDisableCacheChanged));

        /// <summary>
        /// Gets or sets whether caching is disabled.
        /// Default is <see langword="true"/>.
        /// </summary>
        public bool DisableCache
        {
            get => (bool)GetValue(DisableCacheProperty);
            set => SetValue(DisableCacheProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsDragging"/> dependency property key.
        /// </summary>
        protected static readonly DependencyPropertyKey IsDraggingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsDragging), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Identifies the read-only <see cref="IsDragging"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty IsDraggingProperty = IsDraggingPropertyKey.DependencyProperty;

        /// <summary>
        /// Gets whether the operation in progress is dragging.
        /// </summary>
        public bool IsDragging
        {
            get => (bool)GetValue(IsDraggingProperty);
            internal set => SetValue(IsDraggingPropertyKey, value);
        }

        /// <summary>
        /// Identifies the <see cref="RealTimeSelectionEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty RealTimeSelectionEnabledProperty = DependencyProperty.Register(
            nameof(RealTimeSelectionEnabled), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Gets or sets whether real-time selection is enabled.
        /// Default is <see langword="false"/>.
        /// </summary>
        public bool RealTimeSelectionEnabled
        {
            get => (bool)GetValue(RealTimeSelectionEnabledProperty);
            set => SetValue(RealTimeSelectionEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="RealTimeDraggingEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty RealTimeDraggingEnabledProperty = DependencyProperty.Register(
            nameof(RealTimeDraggingEnabled), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Gets or sets whether real-time dragging is enabled.
        /// Default is <see langword="false"/>.
        /// </summary>
        public bool RealTimeDraggingEnabled
        {
            get => (bool)GetValue(RealTimeDraggingEnabledProperty);
            set => SetValue(RealTimeDraggingEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="CanSelectMultipleItems"/> dependency property.
        /// </summary>
        public static DependencyProperty CanSelectMultipleItemsProperty = DependencyProperty.Register(
            nameof(CanSelectMultipleItems), 
            typeof(bool), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(true, OnCanSelectMultipleItemsChanged));

        /// <summary>
        /// Gets or sets whether you can select multiple elements or not.
        /// Default is <see langword="true"/>.
        /// </summary>
        public new bool CanSelectMultipleItems
        {
            get => (bool)GetValue(CanSelectMultipleItemsProperty);
            set => SetValue(CanSelectMultipleItemsProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ViewportSize"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ViewportSizeProperty = DependencyProperty.Register(
            nameof(ViewportSize), 
            typeof(Size), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(Size.Empty, null, CoerceViewportSize));

        // Security: Validate viewport size
        private static object CoerceViewportSize(DependencyObject d, object value)
        {
            if (value is Size size)
            {
                if (double.IsNaN(size.Width) || double.IsInfinity(size.Width) ||
                    double.IsNaN(size.Height) || double.IsInfinity(size.Height) ||
                    size.Width < 0 || size.Height < 0)
                {
                    return Size.Empty;
                }
                
                // Clamp to reasonable maximum size
                double width = Math.Min(100000, size.Width);
                double height = Math.Min(100000, size.Height);
                return new Size(width, height);
            }
            return Size.Empty;
        }

        /// <summary>
        /// Gets the size of the viewport.
        /// </summary>
        public Size ViewportSize
        {
            get => (Size)GetValue(ViewportSizeProperty);
            set => SetValue(ViewportSizeProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ItemsExtent"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty ItemsExtentProperty = DependencyProperty.Register(
            nameof(ItemsExtent), 
            typeof(Rect), 
            typeof(RichCanvas), 
            new FrameworkPropertyMetadata(Rect.Empty, OnItemsExtentChanged, CoerceItemsExtent));

        // Security: Validate items extent
        private static object CoerceItemsExtent(DependencyObject d, object value)
        {
            if (value is Rect rect)
            {
                if (double.IsNaN(rect.Width) || double.IsInfinity(rect.Width) ||
                    double.IsNaN(rect.Height) || double.IsInfinity(rect.Height) ||
                    double.IsNaN(rect.X) || double.IsInfinity(rect.X) ||
                    double.IsNaN(rect.Y) || double.IsInfinity(rect.Y) ||
                    rect.Width < 0 || rect.Height < 0)
                {
                    return Rect.Empty;
                }
                return rect;
            }
            return Rect.Empty;
        }

        /// <summary>
        /// The area covered by the <see cref="RichCanvasContainer"/>s present on <see cref="RichCanvas"/>.
        /// </summary>
        public Rect ItemsExtent
        {
            get => (Rect)GetValue(ItemsExtentProperty);
            set => SetValue(ItemsExtentProperty, value);
        }

        #endregion Properties API

        #region Internal Properties

        internal RichCanvasPanel ItemsHost => _mainPanel;
        internal bool IsZooming { get; set; }
        internal IList BaseSelectedItems => base.SelectedItems;
        internal List<int> CurrentDrawingIndexes { get; } = new List<int>();

        #endregion Internal Properties

        #region Constructors

        static RichCanvas()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(RichCanvas), new FrameworkPropertyMetadata(typeof(RichCanvas)));
            RichCanvasCommands.Register(typeof(RichCanvas));
        }

        /// <summary>
        /// Creates a new instance of <see cref="RichCanvas"/>
        /// </summary>
        public RichCanvas()
        {
            AppliedTransform = new TransformGroup()
            {
                Children = new TransformCollection
                {
                    ScaleTransform, TranslateTransform
                }
            };

            _states = new Stack<CanvasState>();
            _states.Push(GetDefaultState());
            
            // Security: Initialize SelectedItems with a valid collection if null
            if (SelectedItems == null)
            {
                SelectedItems = new List<object>();
            }
        }

        #endregion Constructors

        #region Override Methods

        /// <summary>
        /// Used to returns the implementation of a <see cref="CanvasState"/> used to orchestrate interactions between all defined states.
        /// <br/>
        /// Note: <i>This state is always present on the states stack.</i>
        /// </summary>
        /// <returns>A new <see cref="CanvasState"/></returns>
        public virtual CanvasState GetDefaultState() => new DefaultState(this);

        /// <inheritdoc/>
        public override void OnApplyTemplate()
        {
            _mainPanel = (RichCanvasPanel)GetTemplateChild(DrawingPanelName);
            if (_mainPanel != null)
            {
                _mainPanel.ItemsOwner = this;
                SetCachingMode(DisableCache);
            }
        }

        /// <inheritdoc/>
        protected override AutomationPeer OnCreateAutomationPeer()
            => new RichCanvasAutomationPeer(this);

        /// <inheritdoc/>
        protected override bool IsItemItsOwnContainerOverride(object item) => item is RichCanvasContainer;

        /// <inheritdoc/>
        protected override DependencyObject GetContainerForItemOverride() => new RichCanvasContainer
        {
            RenderTransform = new TransformGroup
            {
                Children = new TransformCollection(new Transform[] { new ScaleTransform(), new TranslateTransform() })
            }
        };

        /// <inheritdoc/>
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            if ((Mouse.Captured == null || IsMouseCaptured) && e.HasAnyButtonPressed())
            {
                if (CurrentState != null && CurrentState.MatchesPreviewMouseDownState(e, out CanvasState matchingState))
                {
                    CaptureMouse();
                    if (matchingState != null)
                    {
                        PushState(matchingState);
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            if ((Mouse.Captured == null || IsMouseCaptured) && e.HasAnyButtonPressed())
            {
                CaptureMouse();
                CurrentState?.HandleMouseDown(e);
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Security: Validate event args
            if (e == null || _mainPanel == null) return;
            
            MousePosition = e.GetPosition(_mainPanel);
            if (IsMouseCaptured)
            {
                CurrentState?.HandleMouseMove(e);
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            if (IsMouseCaptured)
            {
                CurrentState?.HandleMouseUp(e);
                PopState();
                if (e.HasAllButtonsReleased())
                {
                    ReleaseMouseCapture();
                }
            }
            Focus();
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            CurrentState?.HandleKeyDown(e);
        }

        /// <inheritdoc/>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            CurrentState?.HandleKeyUp(e);
            PopState();
        }

        /// <inheritdoc/>
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                CurrentDrawingIndexes.Clear();
                if (CanSelectMultipleItems)
                {
                    base.SelectedItems?.Clear();
                    SelectedItems?.Clear();
                }
                else
                {
                    SelectedItem = null;
                }
            }
            else if (e.NewStartingIndex != -1 && e.Action == NotifyCollectionChangedAction.Add)
            {
                // Security: Validate index bounds
                if (e.NewStartingIndex >= 0 && e.NewStartingIndex < Items.Count)
                {
                    // a container is not able to be drawn if it has Width or Height already
                    var container = ItemContainerGenerator.ContainerFromIndex(e.NewStartingIndex) as RichCanvasContainer;
                    if (container != null && !container.IsValid())
                    {
                        CurrentDrawingIndexes.Add(e.NewStartingIndex);
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                CurrentDrawingIndexes.Remove(e.OldStartingIndex);
                for (int i = e.OldStartingIndex; i < CurrentDrawingIndexes.Count; i++)
                {
                    CurrentDrawingIndexes[i]--;
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Move)
            {
                // Security: Validate index bounds
                if (e.OldStartingIndex >= 0 && e.OldStartingIndex < CurrentDrawingIndexes.Count &&
                    e.NewStartingIndex >= 0)
                {
                    int oldValue = CurrentDrawingIndexes[e.OldStartingIndex];
                    CurrentDrawingIndexes.Remove(oldValue);
                    CurrentDrawingIndexes.Insert(Math.Min(e.NewStartingIndex, CurrentDrawingIndexes.Count), oldValue);
                }
            }
            // Replace event not implemented because the index doesn't change
        }

        /// <inheritdoc />
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            // Security: Validate viewport zoom to prevent division by zero
            double zoom = ViewportZoom;
            if (zoom > 0 && !double.IsNaN(zoom) && !double.IsInfinity(zoom))
            {
                ViewportSize = new Size(ActualWidth / zoom, ActualHeight / zoom);
                UpdateScrollbars();
            }
        }

        #endregion Override Methods

        #region Public Api

        /// <summary>
        /// Get or set whether panning is currently in progress.
        /// </summary>
        public bool IsPanning { get; internal set; }

        /// <summary>Pushes a new state into the stack.</summary>
        /// <param name="state">The new state.</param>
        public void PushState(CanvasState state)
        {
            // Security: Validate state
            if (state == null) return;
            
            _states.Push(state);
            state.Enter();
        }

        /// <summary>Pops the current state from the stack without removing the default one defined by <see cref="GetDefaultState()"/> method.</summary>
        public void PopState()
        {
            // Never remove the default state
            if (_states != null && _states.Count > 1)
            {
                CanvasState prev = _states.Pop();
                prev?.Exit();
                CurrentState?.ReEnter();
            }
        }

        #endregion Public Api

        #region Properties Callbacks

        private static void OnDisableCacheChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var canvas = d as RichCanvas;
            canvas?.SetCachingMode((bool)e.NewValue);
        }

        private static void OnEnableAutoPanningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as RichCanvas;
            canvas?.OnEnableAutoPanningChanged((bool)e.NewValue);
        }

        private static void OnAutoPanTickRateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var canvas = d as RichCanvas;
            canvas?.UpdateTimerInterval();
        }

        private static void OnCanSelectMultipleItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var canvas = d as RichCanvas;
            canvas?.CanSelectMultipleItemsUpdated((bool)e.NewValue);
        }

        private static void OnItemsExtentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = d as RichCanvas;
            editor?.UpdateScrollbars();
        }

        #endregion Properties Callbacks

        #region Selection

        /// <inheritdoc/>
        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            base.OnSelectionChanged(e);
            if (CanSelectMultipleItems)
            {
                IList selected = SelectedItems;
                if (selected != null)
                {
                    IList added = e.AddedItems;
                    if (added != null)
                    {
                        for (int i = 0; i < added.Count; i++)
                        {
                            // Ensure no duplicates are added
                            if (!selected.Contains(added[i]))
                            {
                                selected.Add(added[i]);
                            }
                        }
                    }

                    IList removed = e.RemovedItems;
                    if (removed != null)
                    {
                        for (int i = 0; i < removed.Count; i++)
                        {
                            selected.Remove(removed[i]);
                        }
                    }
                }
            }
            else
            {
                if (e.AddedItems != null && e.AddedItems.Count == 1)
                {
                    SelectedItem = e.AddedItems[0];
                    SelectedItems?.Add(e.AddedItems[0]);
                }
                else if (e.AddedItems != null && e.AddedItems.Count > 1)
                {
                    throw new ArgumentOutOfRangeException($"Cannot select more than 1 item when {nameof(CanSelectMultipleItems)} is set to false.");
                }
                if (e.RemovedItems != null && e.RemovedItems.Count == 1 && 
                    SelectedItems != null && SelectedItems.Count > 0 && 
                    e.RemovedItems[0] == SelectedItems[0])
                {
                    SelectedItems.Remove(e.RemovedItems[0]);
                }
            }
        }

        internal void BeginSelectionTransaction() => BeginUpdateSelectedItems();

        internal void EndSelectionTransaction() => EndUpdateSelectedItems();

        /// <summary>
        /// Returns the elements that intersect with <paramref name="area"/>
        /// </summary>
        /// <param name="area"></param>
        /// <returns></returns>
        public List<object> GetElementsInArea(Rect area)
        {
            var intersectedElements = new List<object>();
            
            // Security: Validate area bounds
            if (double.IsNaN(area.Width) || double.IsInfinity(area.Width) ||
                double.IsNaN(area.Height) || double.IsInfinity(area.Height) ||
                double.IsNaN(area.X) || double.IsInfinity(area.X) ||
                double.IsNaN(area.Y) || double.IsInfinity(area.Y) ||
                area.Width < 0 || area.Height < 0 ||
                _mainPanel == null)
            {
                return intersectedElements;
            }
            
            var rectangleGeometry = new RectangleGeometry(area);
            VisualTreeHelper.HitTest(_mainPanel, null,
                new HitTestResultCallback((HitTestResult result) =>
                {
                    var geometryHitTestResult = result as GeometryHitTestResult;
                    if (geometryHitTestResult != null && 
                        geometryHitTestResult.IntersectionDetail != IntersectionDetail.Empty)
                    {
                        RichCanvasContainer container = VisualHelper.GetParentContainer(geometryHitTestResult.VisualHit);
                        if (container != null && container.DataContext != null)
                        {
                            intersectedElements.Add(container.DataContext);
                        }
                    }
                    return HitTestResultBehavior.Continue;
                }),
                new GeometryHitTestParameters(rectangleGeometry));
            return intersectedElements;
        }

        private static void OnSelectedItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as RichCanvas;
            canvas?.OnSelectedItemsSourceChanged(e.OldValue as IList, e.NewValue as IList);
        }

        private void OnSelectedItemsSourceChanged(IList oldValue, IList newValue)
        {
            if (oldValue is INotifyCollectionChanged oc)
            {
                oc.CollectionChanged -= OnSelectedItemsChanged;
            }

            if (newValue is INotifyCollectionChanged nc)
            {
                nc.CollectionChanged += OnSelectedItemsChanged;
            }

            if (CanSelectMultipleItems)
            {
                IList selectedItems = base.SelectedItems;
                if (selectedItems != null)
                {
                    BeginUpdateSelectedItems();
                    selectedItems.Clear();
                    if (newValue != null)
                    {
                        for (int i = 0; i < newValue.Count; i++)
                        {
                            selectedItems.Add(newValue[i]);
                        }
                    }
                    EndUpdateSelectedItems();
                }
            }
        }

        private void OnSelectedItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    if (CanSelectMultipleItems)
                    {
                        base.SelectedItems?.Clear();
                    }
                    break;

                case NotifyCollectionChangedAction.Add:
                    if (CanSelectMultipleItems)
                    {
                        IList newItems = e.NewItems;
                        if (newItems != null)
                        {
                            IList selectedItems = base.SelectedItems;
                            if (selectedItems != null)
                            {
                                for (int i = 0; i < newItems.Count; i++)
                                {
                                    selectedItems.Add(newItems[i]);
                                }
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (CanSelectMultipleItems)
                    {
                        IList oldItems = e.OldItems;
                        if (oldItems != null)
                        {
                            IList selectedItems = base.SelectedItems;
                            if (selectedItems != null)
                            {
                                for (int i = 0; i < oldItems.Count; i++)
                                {
                                    selectedItems.Remove(oldItems[i]);
                                }
                            }
                        }
                    }
                    break;
            }
        }

        internal void UpdateSingleSelectedItem(RichCanvasContainer selectedContainer)
        {
            if (selectedContainer == null) return;
            
            if (SelectedItem == null)
            {
                selectedContainer.IsSelected = true;
            }
            else
            {
                SelectedItem = null;
                selectedContainer.IsSelected = true;
            }
        }

        #endregion Selection

        #region Handlers And Private Methods

        private void CanSelectMultipleItemsUpdated(bool value)
        {
            base.CanSelectMultipleItems = value;
            if (value)
            {
                if (SelectedItem != null)
                {
                    SelectedItem = null;
                }
            }
            else
            {
                if (SelectedItems?.Count > 1)
                {
                    SelectedItems?.Clear();
                    SelectedItem = null;
                }
                else if (SelectedItems?.Count == 1)
                {
                    SelectedItem = SelectedItems[0];
                }
            }
        }

        private static void OnViewportLocationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var host = d as RichCanvas;
            if (host == null) return;
            
            var translate = (Point)e.NewValue;
            
            // Security: Validate viewport zoom to prevent extreme transforms
            double zoom = host.ViewportZoom;
            if (zoom > 0 && !double.IsNaN(zoom) && !double.IsInfinity(zoom))
            {
                host.TranslateTransform.X = -translate.X * zoom;
                host.TranslateTransform.Y = -translate.Y * zoom;
                host.UpdateScrollbars();
            }
        }

        private void SetCachingMode(bool disable)
        {
            if (_mainPanel != null)
            {
                if (!disable)
                {
                    // Security: Validate viewport zoom for cache scale
                    double zoom = ViewportZoom;
                    if (zoom > 0 && !double.IsNaN(zoom) && !double.IsInfinity(zoom))
                    {
                        _mainPanel.CacheMode = new BitmapCache()
                        {
                            EnableClearType = false,
                            SnapsToDevicePixels = false,
                            RenderAtScale = zoom
                        };
                    }
                }
                else
                {
                    _mainPanel.CacheMode = null;
                }
            }
        }

        private void OnEnableAutoPanningChanged(bool enableAutoPanning)
        {
            if (enableAutoPanning)
            {
                // Security: Validate tick rate before creating timer
                float tickRate = AutoPanTickRate;
                if (tickRate > 0 && !float.IsNaN(tickRate) && !float.IsInfinity(tickRate))
                {
                    if (_autoPanTimer == null)
                    {
                        _autoPanTimer = new DispatcherTimer(
                            TimeSpan.FromMilliseconds(tickRate), 
                            DispatcherPriority.Background, 
                            new EventHandler(HandleAutoPanning), 
                            Dispatcher);
                        _autoPanTimer.Start();
                    }
                    else
                    {
                        _autoPanTimer.Interval = TimeSpan.FromMilliseconds(tickRate);
                        _autoPanTimer.Start();
                    }
                }
            }
            else
            {
                _autoPanTimer?.Stop();
            }
        }

        private void HandleAutoPanning(object sender, EventArgs e)
        {
            if (IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed && 
                Mouse.Captured != null && !IsMouseCapturedByScrollBar() && !IsPanning)
            {
                Point mousePosition = Mouse.GetPosition(this);
                double x = ViewportLocation.X;
                double y = ViewportLocation.Y;

                // Security: Use validated auto-pan speed
                float speed = AutoPanSpeed;

                if (mousePosition.Y <= 0)
                {
                    y -= speed;
                }
                else if (mousePosition.Y >= ViewportHeight)
                {
                    y += speed;
                }

                if (mousePosition.X <= 0)
                {
                    x -= speed;
                }
                else if (mousePosition.X >= ViewportWidth)
                {
                    x += speed;
                }

                ViewportLocation = new Point(x, y);
                if (_mainPanel != null)
                {
                    MousePosition = Mouse.GetPosition(_mainPanel);
                }

                CurrentState?.HandleAutoPanning(new MouseEventArgs(Mouse.PrimaryDevice, 0));
            }
        }

        private static bool IsMouseCapturedByScrollBar()
        {
            var captured = Mouse.Captured;
            return captured != null && 
                   (captured.GetType() == typeof(Thumb) || captured.GetType() == typeof(RepeatButton));
        }

        private void UpdateTimerInterval()
        {
            if (_autoPanTimer != null)
            {
                // Security: Validate tick rate
                float tickRate = AutoPanTickRate;
                if (tickRate > 0 && !float.IsNaN(tickRate) && !float.IsInfinity(tickRate))
                {
                    _autoPanTimer.Interval = TimeSpan.FromMilliseconds(tickRate);
                }
            }
        }

        internal void RaiseDrawEndedEvent(object context, Point mousePosition)
        {
            var newEventArgs = new RoutedEventArgs(DrawingEndedEvent, new DrawEndedEventArgs(context, mousePosition));
            RaiseEvent(newEventArgs);
        }

        #endregion Handlers And Private Methods
    }
}