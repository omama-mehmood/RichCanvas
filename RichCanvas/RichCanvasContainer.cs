using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using RichCanvas.Helpers;
using RichCanvas.States.ContainerStates;

namespace RichCanvas
{
    /// <summary>
    /// Delegate used to notify when an <see cref="RichCanvasContainer"/> is dragged.
    /// </summary>
    /// <param name="newLocation">The new location.</param>
    public delegate void PreviewLocationChanged(Point newLocation);

    /// <summary>
    /// <see cref="RichCanvas"/> items container.
    /// </summary>
    [TemplatePart(Name = ContentPresenterName, Type = typeof(ContentPresenter))]
    public class RichCanvasContainer : ContentControl
    {
        private const string ContentPresenterName = "PART_ContentPresenter";
        private Stack<ContainerState> _states;

        /// <summary>
        /// Default fallback value for container width used on drawing if the set value is 0.
        /// </summary>
        public const double DefaultWidth = 1d;

        /// <summary>
        /// Default fallback value for container height used on drawing if the set value is 0.
        /// </summary>
        public const double DefaultHeight = 1d;

        // Security: Define safe bounds for properties
        private const double MaxCoordinate = 100000d;
        private const double MinCoordinate = -100000d;
        private const double MaxScale = 100d;
        private const double MinScale = 0.01d;

        internal ScaleTransform ScaleTransform => RenderTransform is TransformGroup group ? group.Children.OfType<ScaleTransform>().FirstOrDefault() : null;
        internal TranslateTransform TranslateTransform => RenderTransform is TransformGroup group ? group.Children.OfType<TranslateTransform>().FirstOrDefault() : null;

        #region Properties API

        /// <summary>
        /// Identifies the <see cref="IsSelected"/> dependency property.
        /// </summary>
        public static DependencyProperty IsSelectedProperty = Selector.IsSelectedProperty.AddOwner(
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsSelectedChanged));

        /// <summary>
        /// Gets or sets a value that indicates whether this item is selected.
        /// Can only be set if <see cref="IsSelectable"/> is true.
        /// </summary>
        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Top"/> dependency property.
        /// </summary>
        public static DependencyProperty TopProperty = DependencyProperty.Register(
            nameof(Top), 
            typeof(double), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(0d, OnPositionChanged, CoercePosition));

        // Security: Validate position to prevent extreme coordinates
        private static object CoercePosition(DependencyObject d, object value)
        {
            if (value is double doubleValue)
            {
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                    return 0d;
                    
                return Math.Max(MinCoordinate, Math.Min(MaxCoordinate, doubleValue));
            }
            return 0d;
        }

        /// <summary>
        /// Gets or sets the Top position of this <see cref="RichCanvasContainer"/> on <see cref="RichCanvas.ItemsHost"/>
        /// </summary>
        public double Top
        {
            get => (double)GetValue(TopProperty);
            set => SetValue(TopProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Left"/> dependency property.
        /// </summary>
        public static DependencyProperty LeftProperty = DependencyProperty.Register(
            nameof(Left), 
            typeof(double), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(0d, OnPositionChanged, CoercePosition));

        /// <summary>
        /// Gets or sets the Left position of this <see cref="RichCanvasContainer"/> on <see cref="RichCanvas.ItemsHost"/>
        /// </summary>
        public double Left
        {
            get => (double)GetValue(LeftProperty);
            set => SetValue(LeftProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsSelectable"/> dependency property.
        /// </summary>
        public static DependencyProperty IsSelectableProperty = DependencyProperty.Register(
            nameof(IsSelectable), 
            typeof(bool), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(true));

        /// <summary>
        /// Gets or sets whether this <see cref="RichCanvasContainer"/> can be selected.
        /// True by default
        /// </summary>
        public bool IsSelectable
        {
            get => (bool)GetValue(IsSelectableProperty);
            set => SetValue(IsSelectableProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsDraggable"/> dependency property.
        /// </summary>
        public static DependencyProperty IsDraggableProperty = DependencyProperty.Register(
            nameof(IsDraggable), 
            typeof(bool), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(true));

        /// <summary>
        /// Gets or sets whether this <see cref="RichCanvasContainer"/> can be dragged on <see cref="RichCanvas.ItemsHost"/>
        /// True by default
        /// </summary>
        public bool IsDraggable
        {
            get => (bool)GetValue(IsDraggableProperty);
            set => SetValue(IsDraggableProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="HasCustomBehavior"/> dependency property.
        /// </summary>
        public static DependencyProperty HasCustomBehaviorProperty = DependencyProperty.Register(
            nameof(HasCustomBehavior), 
            typeof(bool), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(false));

        /// <summary>
        /// Gets or sets whether this <see cref="RichCanvasContainer"/> has custom behavior handled out of dragging
        /// This tells <see cref="RichCanvas"/> to stop handling mouse interaction when manipulating this <see cref="RichCanvasContainer"/>
        /// False by default
        /// </summary>
        public bool HasCustomBehavior
        {
            get => (bool)GetValue(HasCustomBehaviorProperty);
            set => SetValue(HasCustomBehaviorProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="ShouldBringIntoView"/> dependency property.
        /// </summary>
        public static DependencyProperty ShouldBringIntoViewProperty = DependencyProperty.Register(
            nameof(ShouldBringIntoView), 
            typeof(bool), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(false, OnBringIntoViewChanged));

        /// <summary>
        /// Gets or sets whether this <see cref="RichCanvasContainer"/> should be centered inside <see cref="RichCanvas"/> viewport.
        /// </summary>
        public bool ShouldBringIntoView
        {
            get => (bool)GetValue(ShouldBringIntoViewProperty);
            set => SetValue(ShouldBringIntoViewProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Scale"/> dependency property.
        /// </summary>
        public static DependencyProperty ScaleProperty = DependencyProperty.Register(
            nameof(Scale), 
            typeof(Point), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(new Point(1, 1), OnScaleChanged, CoerceScale));

        // Security: Validate scale to prevent extreme transforms
        private static object CoerceScale(DependencyObject d, object value)
        {
            if (value is Point point)
            {
                // Prevent NaN and infinite values
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Y) || double.IsInfinity(point.Y))
                {
                    return new Point(1, 1);
                }
                
                // Clamp scale to reasonable bounds to prevent extreme transforms
                double x = Math.Max(MinScale, Math.Min(MaxScale, Math.Abs(point.X)));
                double y = Math.Max(MinScale, Math.Min(MaxScale, Math.Abs(point.Y)));
                
                // Preserve sign for flipping
                x = point.X < 0 ? -x : x;
                y = point.Y < 0 ? -y : y;
                
                return new Point(x, y);
            }
            return new Point(1, 1);
        }

        /// <summary>
        /// Gets or sets this <see cref="RichCanvasContainer"/> ScaleTransform in order to get direction.
        /// </summary>
        public Point Scale
        {
            get => (Point)GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="AllowScaleChangeToUpdatePosition"/> dependency property.
        /// </summary>
        public static DependencyProperty AllowScaleChangeToUpdatePositionProperty = DependencyProperty.Register(
            nameof(AllowScaleChangeToUpdatePosition), 
            typeof(bool), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(true));

        /// <summary>
        /// Gets or sets whether this <see cref="RichCanvasContainer"/> Left and Top can be updated while Drawing if the <see cref="Scale"/> is changed.
        /// </summary>
        public bool AllowScaleChangeToUpdatePosition
        {
            get => (bool)GetValue(AllowScaleChangeToUpdatePositionProperty);
            set => SetValue(AllowScaleChangeToUpdatePositionProperty, value);
        }

        /// <summary>
        /// Apply transforms on <see cref="RichCanvasContainer"/>
        /// </summary>
        public static DependencyProperty ApplyTransformProperty = DependencyProperty.RegisterAttached(
            "ApplyTransform", 
            typeof(Transform), 
            typeof(RichCanvasContainer), 
            new FrameworkPropertyMetadata(default(Transform), OnApplyTransformChanged, CoerceTransform));

        // Security: Validate transform to prevent malicious transforms
        private static object CoerceTransform(DependencyObject d, object value)
        {
            if (value == null)
                return null;
                
            if (value is Transform transform)
            {
                // Validate transform is safe
                if (!IsTransformSafe(transform))
                {
                    // Return identity transform if unsafe
                    return Transform.Identity;
                }
                return transform;
            }
            
            return Transform.Identity;
        }

        // Security: Helper method to validate transforms
        private static bool IsTransformSafe(Transform transform)
        {
            if (transform == null)
                return true;
                
            // Check for extreme matrix values that could cause rendering issues
            try
            {
                var matrix = transform.Value;
                
                // Check for NaN or Infinity in matrix values
                if (double.IsNaN(matrix.M11) || double.IsInfinity(matrix.M11) ||
                    double.IsNaN(matrix.M12) || double.IsInfinity(matrix.M12) ||
                    double.IsNaN(matrix.M21) || double.IsInfinity(matrix.M21) ||
                    double.IsNaN(matrix.M22) || double.IsInfinity(matrix.M22) ||
                    double.IsNaN(matrix.OffsetX) || double.IsInfinity(matrix.OffsetX) ||
                    double.IsNaN(matrix.OffsetY) || double.IsInfinity(matrix.OffsetY))
                {
                    return false;
                }
                
                // Check for extreme scale factors
                double scaleX = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M21 * matrix.M21);
                double scaleY = Math.Sqrt(matrix.M12 * matrix.M12 + matrix.M22 * matrix.M22);
                
                if (scaleX > MaxScale || scaleY > MaxScale || scaleX < MinScale || scaleY < MinScale)
                {
                    return false;
                }
                
                // Check for extreme offsets
                if (Math.Abs(matrix.OffsetX) > MaxCoordinate || Math.Abs(matrix.OffsetY) > MaxCoordinate)
                {
                    return false;
                }
                
                return true;
            }
            catch
            {
                // If any error occurs during validation, consider it unsafe
                return false;
            }
        }

        /// <summary>
        /// Sets a property value that tells what <see cref="Transform"/> should be applied on <see cref="RichCanvasContainer"/>.RenderTransform property.
        /// </summary>
        /// <param name="element"></param>
        /// <param name="value"></param>
        public static void SetApplyTransform(UIElement element, Transform value) 
        {
            if (element != null)
            {
                element.SetValue(ApplyTransformProperty, value);
            }
        }

        /// <summary>
        /// Gets the <see cref="RichCanvasContainer"/>.ApplyTransform attached property value that indicates the current <see cref="RichCanvasContainer"/>.RenderTransform.
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static Transform GetApplyTransform(UIElement element) 
        {
            if (element != null)
            {
                return (Transform)element.GetValue(ApplyTransformProperty);
            }
            return Transform.Identity;
        }

        /// <summary>
        /// Identifies the <see cref="Selected"/> routed event.
        /// </summary>
        public static readonly RoutedEvent SelectedEvent = Selector.SelectedEvent.AddOwner(typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs whenever this <see cref="RichCanvasContainer"/> is selected.
        /// </summary>
        public event RoutedEventHandler Selected
        {
            add => AddHandler(SelectedEvent, value);
            remove => RemoveHandler(SelectedEvent, value);
        }

        /// <summary>
        /// Identifies the <see cref="Unselected"/> routed event.
        /// </summary>
        public static readonly RoutedEvent UnselectedEvent = Selector.UnselectedEvent.AddOwner(typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs when this <see cref="RichCanvasContainer"/> is unselected.
        /// </summary>
        public event RoutedEventHandler Unselected
        {
            add => AddHandler(UnselectedEvent, value);
            remove => RemoveHandler(UnselectedEvent, value);
        }

        /// <summary>
        /// Identifies the <see cref="TopChanged"/> routed event.
        /// </summary>
        public static readonly RoutedEvent TopChangedEvent = EventManager.RegisterRoutedEvent(
            nameof(TopChanged), 
            RoutingStrategy.Bubble, 
            typeof(RoutedEventHandler), 
            typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs whenever <see cref="Top"/> changes.
        /// </summary>
        public event RoutedEventHandler TopChanged
        {
            add { AddHandler(TopChangedEvent, value); }
            remove { RemoveHandler(TopChangedEvent, value); }
        }

        /// <summary>
        /// Identifies the <see cref="LeftChanged"/> routed event.
        /// </summary>
        public static readonly RoutedEvent LeftChangedEvent = EventManager.RegisterRoutedEvent(
            nameof(LeftChanged), 
            RoutingStrategy.Bubble, 
            typeof(RoutedEventHandler), 
            typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs whenever <see cref="Left"/> changes.
        /// </summary>
        public event RoutedEventHandler LeftChanged
        {
            add { AddHandler(LeftChangedEvent, value); }
            remove { RemoveHandler(LeftChangedEvent, value); }
        }

        /// <summary>
        /// Identifies the <see cref="DragStarted"/> routed event.
        /// </summary>
        public static readonly RoutedEvent DragStartedEvent = EventManager.RegisterRoutedEvent(
            nameof(DragStarted), 
            RoutingStrategy.Bubble, 
            typeof(DragStartedEventHandler), 
            typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs when this <see cref="RichCanvasContainer"/> is the instigator of a drag operation.
        /// </summary>
        public event DragStartedEventHandler DragStarted
        {
            add => AddHandler(DragStartedEvent, value);
            remove => RemoveHandler(DragStartedEvent, value);
        }

        /// <summary>
        /// Identifies the <see cref="DragDelta"/> routed event.
        /// </summary>
        public static readonly RoutedEvent DragDeltaEvent = EventManager.RegisterRoutedEvent(
            nameof(DragDelta), 
            RoutingStrategy.Bubble, 
            typeof(DragDeltaEventHandler), 
            typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs when this <see cref="RichCanvasContainer"/> is being dragged.
        /// </summary>
        public event DragDeltaEventHandler DragDelta
        {
            add => AddHandler(DragDeltaEvent, value);
            remove => RemoveHandler(DragDeltaEvent, value);
        }

        /// <summary>
        /// Identifies the <see cref="DragCompleted"/> routed event.
        /// </summary>
        public static readonly RoutedEvent DragCompletedEvent = EventManager.RegisterRoutedEvent(
            nameof(DragCompleted), 
            RoutingStrategy.Bubble, 
            typeof(DragCompletedEventHandler), 
            typeof(RichCanvasContainer));

        /// <summary>
        /// Occurs when this <see cref="RichCanvasContainer"/> completed the drag operation.
        /// </summary>
        public event DragCompletedEventHandler DragCompleted
        {
            add => AddHandler(DragCompletedEvent, value);
            remove => RemoveHandler(DragCompletedEvent, value);
        }

        #endregion Properties API

        static RichCanvasContainer()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(RichCanvasContainer), new FrameworkPropertyMetadata(typeof(RichCanvasContainer)));
        }

        /// <summary>
        /// Gets this <see cref="RichCanvasContainer"/> TransformBounds.
        /// </summary>
        public Rect BoundingBox { get; private set; }

        /// <summary>
        /// Current state of <see cref="RichCanvasContainer"/>.
        /// </summary>
        public ContainerState CurrentState => _states?.Count > 0 ? _states.Peek() : null;

        private RichCanvas _host;

        /// <summary>
        /// The <see cref="RichCanvas"/> that owns this <see cref="RichCanvasContainer"/>.
        /// </summary>
        public RichCanvas Host => _host ??= (RichCanvas)ItemsControl.ItemsControlFromItemContainer(this);

        internal bool TopPropertyInitalized { get; private set; }
        internal bool LeftPropertyInitialized { get; private set; }

        /// <summary>
        /// Initializes a new instance of <see cref="RichCanvasContainer"/> class.
        /// </summary>
        public RichCanvasContainer()
        {
            _states = new Stack<ContainerState>();
            _states.Push(GetDefaultState());
        }

        /// <summary>
        /// Calculates <see cref="RichCanvasContainer"/> bounding box based on applied transforms.
        /// </summary>
        public void CalculateBoundingBox()
        {
            try
            {
                if (Host?.ItemsHost == null)
                {
                    BoundingBox = Rect.Empty;
                    return;
                }

                GeneralTransform transform = TransformToVisual(Host.ItemsHost);
                
                // Security: Validate dimensions before calculating bounds
                double width = Width;
                double height = Height;
                double actualWidth = ActualWidth;
                double actualHeight = ActualHeight;
                
                if (double.IsNaN(width) || double.IsNaN(height))
                {
                    // Use actual dimensions if Width/Height are not set
                    if (!double.IsNaN(actualWidth) && !double.IsInfinity(actualWidth) &&
                        !double.IsNaN(actualHeight) && !double.IsInfinity(actualHeight) &&
                        actualWidth >= 0 && actualHeight >= 0 &&
                        actualWidth < MaxCoordinate && actualHeight < MaxCoordinate)
                    {
                        Rect actualBounds = transform.TransformBounds(new Rect(0, 0, actualWidth, actualHeight));
                        BoundingBox = ValidateBounds(actualBounds);
                    }
                    else
                    {
                        BoundingBox = Rect.Empty;
                    }
                }
                else if (!double.IsInfinity(width) && !double.IsInfinity(height) &&
                         width >= 0 && height >= 0 &&
                         width < MaxCoordinate && height < MaxCoordinate)
                {
                    Rect bounds = transform.TransformBounds(new Rect(0, 0, width, height));
                    BoundingBox = ValidateBounds(bounds);
                }
                else
                {
                    BoundingBox = Rect.Empty;
                }
            }
            catch
            {
                // Security: If any error occurs, use empty bounds
                BoundingBox = Rect.Empty;
            }
        }

        // Security: Helper method to validate calculated bounds
        private Rect ValidateBounds(Rect bounds)
        {
            // Check for invalid values
            if (double.IsNaN(bounds.X) || double.IsInfinity(bounds.X) ||
                double.IsNaN(bounds.Y) || double.IsInfinity(bounds.Y) ||
                double.IsNaN(bounds.Width) || double.IsInfinity(bounds.Width) ||
                double.IsNaN(bounds.Height) || double.IsInfinity(bounds.Height) ||
                bounds.Width < 0 || bounds.Height < 0)
            {
                return Rect.Empty;
            }
            
            // Clamp to reasonable bounds
            double x = Math.Max(MinCoordinate, Math.Min(MaxCoordinate, bounds.X));
            double y = Math.Max(MinCoordinate, Math.Min(MaxCoordinate, bounds.Y));
            double width = Math.Min(MaxCoordinate, bounds.Width);
            double height = Math.Min(MaxCoordinate, bounds.Height);
            
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Used to returns the implementation of a <see cref="ContainerState"/> used to orchestrate interactions between all defined states.
        /// <br/>
        /// Note: <i>This state is always present on the states stack.</i>
        /// </summary>
        /// <returns>A new <see cref="ContainerState"/></returns>
        protected virtual ContainerState GetDefaultState() => new ContainerDefaultState(this);

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
            Focus();
            if (Mouse.Captured == null || IsMouseCaptured)
            {
                CaptureMouse();
                CurrentState?.HandleMouseDown(e);
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Security: Validate event args
            if (e == null) return;
            
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
            
            // Release the mouse capture if all the mouse buttons are released
            if (IsMouseCaptured && e.RightButton == MouseButtonState.Released && 
                e.LeftButton == MouseButtonState.Released && e.MiddleButton == MouseButtonState.Released)
            {
                CurrentState?.HandleMouseUp(e);
                PopState();
                ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// Occurs when the <see cref="RichCanvasContainer"/> is being dragged.
        /// </summary>
        public event PreviewLocationChanged PreviewLocationChanged;

        /// <summary>
        /// Raises the <see cref="PreviewLocationChanged"/> event.
        /// </summary>
        /// <param name="location">The new location.</param>
        protected internal void OnPreviewLocationChanged(Point location)
        {
            // Security: Validate location before raising event
            if (!double.IsNaN(location.X) && !double.IsInfinity(location.X) &&
                !double.IsNaN(location.Y) && !double.IsInfinity(location.Y))
            {
                PreviewLocationChanged?.Invoke(location);
            }
        }

        /// <summary>Pushes a new state into the stack.</summary>
        /// <param name="state">The new state.</param>
        public void PushState(ContainerState state)
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
                ContainerState prev = _states.Pop();
                prev?.Exit();
                CurrentState?.ReEnter();
            }
        }

        internal bool IsValid()
        {
            // Security: Enhanced validation with bounds checking
            double width = Width;
            double height = Height;
            double actualWidth = ActualWidth;
            double actualHeight = ActualHeight;
            
            bool hasValidDimensions = (height != 0 || actualHeight != 0) && (width != 0 || actualWidth != 0);
            bool hasFiniteDimensions = (!double.IsNaN(height) || !double.IsNaN(actualHeight)) && 
                                       (!double.IsNaN(width) || !double.IsNaN(actualWidth));
            bool hasReasonableDimensions = (!double.IsInfinity(height) && !double.IsInfinity(actualHeight)) &&
                                          (!double.IsInfinity(width) && !double.IsInfinity(actualWidth));
            
            return hasValidDimensions && hasFiniteDimensions && hasReasonableDimensions;
        }

        internal void RaiseDragStartedEvent(Point position)
        {
            // Security: Validate position before raising event
            if (double.IsNaN(position.X) || double.IsInfinity(position.X) ||
                double.IsNaN(position.Y) || double.IsInfinity(position.Y))
            {
                position = new Point(0, 0);
            }
            
            RaiseEvent(new DragStartedEventArgs(position.X, position.Y)
            {
                RoutedEvent = DragStartedEvent
            });
        }

        internal void RaiseDragDeltaEvent(Point position)
        {
            // Security: Validate position before raising event
            if (double.IsNaN(position.X) || double.IsInfinity(position.X) ||
                double.IsNaN(position.Y) || double.IsInfinity(position.Y))
            {
                position = new Point(0, 0);
            }
            
            RaiseEvent(new DragDeltaEventArgs(position.X, position.Y)
            {
                RoutedEvent = DragDeltaEvent
            });
        }

        internal void RaiseDragCompletedEvent(Point position)
        {
            // Security: Validate position before raising event
            if (double.IsNaN(position.X) || double.IsInfinity(position.X) ||
                double.IsNaN(position.Y) || double.IsInfinity(position.Y))
            {
                position = new Point(0, 0);
            }
            
            RaiseEvent(new DragCompletedEventArgs(position.X, position.Y, false)
            {
                RoutedEvent = DragCompletedEvent
            });
        }

        private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var container = d as RichCanvasContainer;
            if (container != null && e.NewValue is Point point)
            {
                container.OverrideScale(point);
            }
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            var container = d as RichCanvasContainer;
            container?.UpdatePosition(e.Property);
        }

        private static void OnApplyTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) 
        {
            if (d != null)
            {
                var container = VisualHelper.GetParentContainer(d);
                if (container != null && e.NewValue is Transform transform)
                {
                    container.ApplyTransform(transform);
                }
            }
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var elem = d as RichCanvasContainer;
            if (elem != null && e.NewValue is bool newValue)
            {
                bool result = elem.IsSelectable && newValue;
                elem.OnSelectedChanged(result);
                elem.IsSelected = result;
            }
        }

        private static void OnBringIntoViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool shouldBring && shouldBring)
            {
                var container = d as RichCanvasContainer;
                container?.BringIntoView();
            }
        }

        private void UpdatePosition(DependencyProperty prop)
        {
            if (prop?.Name == nameof(Top) && !TopPropertyInitalized)
            {
                TopPropertyInitalized = true;
            }
            if (prop?.Name == nameof(Left) && !LeftPropertyInitialized)
            {
                LeftPropertyInitialized = true;
            }
            
            // Security: Validate values before raising events
            double top = Top;
            double left = Left;
            
            if (!double.IsNaN(top) && !double.IsInfinity(top))
            {
                RaiseEvent(new RoutedEventArgs(TopChangedEvent, top));
            }
            
            if (!double.IsNaN(left) && !double.IsInfinity(left))
            {
                RaiseEvent(new RoutedEventArgs(LeftChangedEvent, left));
            }
            
            Host?.ItemsHost?.InvalidateArrange();
        }

        private void OnSelectedChanged(bool value)
        {
            // Raise event after the selection operation ended
            if (Host != null && (!Host.IsSelecting || Host.RealTimeSelectionEnabled))
            {
                // Add to base SelectedItems
                RaiseEvent(new RoutedEventArgs(value ? SelectedEvent : UnselectedEvent, this));
            }
        }

        private void ApplyTransform(Transform apply)
        {
            // Security: Validate and clone transform
            if (apply != null && IsTransformSafe(apply))
            {
                try
                {
                    RenderTransform = apply.Clone();
                    if (IsValid())
                    {
                        // Invalidate arrange to calculate correct BoundingBox
                        Host?.ItemsHost?.InvalidateArrange();
                    }
                }
                catch
                {
                    // Security: If cloning fails, use identity transform
                    RenderTransform = Transform.Identity;
                }
            }
            else
            {
                RenderTransform = Transform.Identity;
            }
        }

        private void OverrideScale(Point value)
        {
            var scaleTransform = ScaleTransform;
            if (scaleTransform != null)
            {
                // Security: Values are already validated through coercion
                scaleTransform.ScaleX = value.X;
                scaleTransform.ScaleY = value.Y;
            }
        }
    }
}