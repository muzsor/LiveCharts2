// The MIT License(MIT)

// Copyright(c) 2021 Alberto Rodriguez Orozco & LiveCharts Contributors

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveChartsCore.Kernel;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Collections.ObjectModel;
using Avalonia.Media;
using A = Avalonia;
using Avalonia.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Templates;
using System.Linq;

namespace LiveChartsCore.SkiaSharp.Avalonia
{
    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}" />
    public class CartesianChart : UserControl, ICartesianChartView<SkiaSharpDrawingContext>, IAvaloniaChart
    {
        #region fields

        /// <summary>
        /// The core
        /// </summary>
        protected Chart<SkiaSharpDrawingContext>? core;

        /// <summary>
        /// The legend
        /// </summary>
        protected IChartLegend<SkiaSharpDrawingContext>? legend;

        /// <summary>
        /// The tooltip
        /// </summary>
        protected IChartTooltip<SkiaSharpDrawingContext>? tooltip;

        private readonly ActionThrottler mouseMoveThrottler;
        private PointF mousePosition = new();
        private readonly CollectionDeepObserver<ISeries> seriesObserver;
        private readonly CollectionDeepObserver<IAxis> xObserver;
        private readonly CollectionDeepObserver<IAxis> yObserver;
        private readonly ActionThrottler panningThrottler;
        private A.Point? previous;
        private A.Point? current;
        private bool isPanning = false;

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="CartesianChart"/> class.
        /// </summary>
        /// <exception cref="Exception">Default colors are not valid</exception>
        public CartesianChart()
        {
            InitializeComponent();

            if (!LiveCharts.IsConfigured) LiveCharts.Configure(LiveChartsSkiaSharp.DefaultPlatformBuilder);

            var stylesBuilder = LiveCharts.CurrentSettings.GetStylesBuilder<SkiaSharpDrawingContext>();
            var initializer = stylesBuilder.GetInitializer();
            if (stylesBuilder.CurrentColors == null || stylesBuilder.CurrentColors.Length == 0)
                throw new Exception("Default colors are not valid");
            initializer.ConstructChart(this);

            InitializeCore();

            mouseMoveThrottler = new ActionThrottler(MouseMoveThrottlerUnlocked, TimeSpan.FromMilliseconds(10));

            seriesObserver = new CollectionDeepObserver<ISeries>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
            xObserver = new CollectionDeepObserver<IAxis>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
            yObserver = new CollectionDeepObserver<IAxis>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);

            XAxes = new List<IAxis>() { new Axis() };
            YAxes = new List<IAxis>() { new Axis() };
            Series = new ObservableCollection<ISeries>();

            // workaround to deteck mouse events.
            // avalonia do not seem to detect events if brackground is not set.
            Background = new SolidColorBrush(Colors.Transparent);

            PointerWheelChanged += CartesianChart_PointerWheelChanged;
            PointerPressed += CartesianChart_PointerPressed;
            PointerMoved += CartesianChart_PointerMoved;

            panningThrottler = new ActionThrottler(DoPan, TimeSpan.FromMilliseconds(30));
        }

        #region avalonia/dependency properties

        /// <summary>
        /// The draw margin property
        /// </summary>
        public static readonly AvaloniaProperty<Margin?> DrawMarginProperty =
           AvaloniaProperty.Register<CartesianChart, Margin?>(nameof(DrawMargin), null, inherits: true);

        /// <summary>
        /// The series property
        /// </summary>
        public static readonly AvaloniaProperty<IEnumerable<ISeries>> SeriesProperty =
           AvaloniaProperty.Register<CartesianChart, IEnumerable<ISeries>>(nameof(Series), Enumerable.Empty<ISeries>(), inherits: true);

        /// <summary>
        /// The x axes property
        /// </summary>
        public static readonly AvaloniaProperty<IEnumerable<IAxis>> XAxesProperty =
            AvaloniaProperty.Register<CartesianChart, IEnumerable<IAxis>>(nameof(XAxes), Enumerable.Empty<IAxis>(), inherits: true);

        /// <summary>
        /// The y axes property
        /// </summary>
        public static readonly AvaloniaProperty<IEnumerable<IAxis>> YAxesProperty =
            AvaloniaProperty.Register<CartesianChart, IEnumerable<IAxis>>(nameof(YAxes), Enumerable.Empty<IAxis>(), inherits: true);

        /// <summary>
        /// The zoom mode property
        /// </summary>
        public static readonly AvaloniaProperty<ZoomAndPanMode> ZoomModeProperty =
            AvaloniaProperty.Register<CartesianChart, ZoomAndPanMode>(
                nameof(ZoomMode), LiveCharts.CurrentSettings.DefaultZoomMode, inherits: true);

        /// <summary>
        /// The zooming speed property
        /// </summary>
        public static readonly AvaloniaProperty<double> ZoomingSpeedProperty =
            AvaloniaProperty.Register<CartesianChart, double>(
                nameof(ZoomingSpeed), LiveCharts.CurrentSettings.DefaultZoomSpeed, inherits: true);

        /// <summary>
        /// The animations speed property
        /// </summary>
        public static readonly AvaloniaProperty<TimeSpan> AnimationsSpeedProperty =
            AvaloniaProperty.Register<CartesianChart, TimeSpan>(
                nameof(AnimationsSpeed), LiveCharts.CurrentSettings.DefaultAnimationsSpeed, inherits: true);

        /// <summary>
        /// The easing function property
        /// </summary>
        public static readonly AvaloniaProperty<Func<float, float>> EasingFunctionProperty =
            AvaloniaProperty.Register<CartesianChart, Func<float, float>>(
                nameof(AnimationsSpeed), LiveCharts.CurrentSettings.DefaultEasingFunction, inherits: true);

        /// <summary>
        /// The tool tip template property
        /// </summary>
        public static readonly AvaloniaProperty<DataTemplate?> TooltipTemplateProperty =
            AvaloniaProperty.Register<CartesianChart, DataTemplate?>(nameof(TooltipTemplate), null, inherits: true);

        /// <summary>
        /// The tool tip position property
        /// </summary>
        public static readonly AvaloniaProperty<TooltipPosition> TooltipPositionProperty =
            AvaloniaProperty.Register<CartesianChart, TooltipPosition>(
                nameof(TooltipPosition), LiveCharts.CurrentSettings.DefaultTooltipPosition, inherits: true);

        /// <summary>
        /// The tool tip finding strategy property
        /// </summary>
        public static readonly AvaloniaProperty<TooltipFindingStrategy> TooltipFindingStrategyProperty =
            AvaloniaProperty.Register<CartesianChart, TooltipFindingStrategy>(
                nameof(LegendPosition), LiveCharts.CurrentSettings.DefaultTooltipFindingStrategy, inherits: true);

        /// <summary>
        /// The tool tip font family property
        /// </summary>
        public static readonly AvaloniaProperty<A.Media.FontFamily> TooltipFontFamilyProperty =
            AvaloniaProperty.Register<CartesianChart, A.Media.FontFamily>(
                nameof(TooltipFontFamily), new A.Media.FontFamily("Arial"), inherits: true);

        /// <summary>
        /// The tool tip font size property
        /// </summary>
        public static readonly AvaloniaProperty<double> TooltipFontSizeProperty =
            AvaloniaProperty.Register<CartesianChart, double>(nameof(TooltipFontSize), 13d, inherits: true);

        /// <summary>
        /// The tool tip font weight property
        /// </summary>
        public static readonly AvaloniaProperty<FontWeight> TooltipFontWeightProperty =
            AvaloniaProperty.Register<CartesianChart, FontWeight>(nameof(TooltipFontWeight), FontWeight.Normal, inherits: true);

        /// <summary>
        /// The tool tip font style property
        /// </summary>
        public static readonly AvaloniaProperty<A.Media.FontStyle> TooltipFontStyleProperty =
            AvaloniaProperty.Register<CartesianChart, A.Media.FontStyle>(
                nameof(TooltipFontStyle), A.Media.FontStyle.Normal, inherits: true);

        /// <summary>
        /// The tool tip text brush property
        /// </summary>
        public static readonly AvaloniaProperty<SolidColorBrush> TooltipTextBrushProperty =
            AvaloniaProperty.Register<CartesianChart, SolidColorBrush>(
                nameof(TooltipTextBrush), new SolidColorBrush(new A.Media.Color(255, 35, 35, 35)), inherits: true);

        /// <summary>
        /// The tool tip background property
        /// </summary>
        public static readonly AvaloniaProperty<IBrush> TooltipBackgroundProperty =
            AvaloniaProperty.Register<CartesianChart, IBrush>(nameof(TooltipBackground),
                new SolidColorBrush(new A.Media.Color(255, 250, 250, 250)), inherits: true);

        /// <summary>
        /// The legend position property
        /// </summary>
        public static readonly AvaloniaProperty<LegendPosition> LegendPositionProperty =
            AvaloniaProperty.Register<CartesianChart, LegendPosition>(
                nameof(LegendPosition), LiveCharts.CurrentSettings.DefaultLegendPosition, inherits: true);

        /// <summary>
        /// The legend orientation property
        /// </summary>
        public static readonly AvaloniaProperty<LegendOrientation> LegendOrientationProperty =
            AvaloniaProperty.Register<CartesianChart, LegendOrientation>(
                nameof(LegendOrientation), LiveCharts.CurrentSettings.DefaultLegendOrientation, inherits: true);

        /// <summary>
        /// The legend template property
        /// </summary>
        public static readonly AvaloniaProperty<DataTemplate?> LegendTemplateProperty =
            AvaloniaProperty.Register<CartesianChart, DataTemplate?>(nameof(LegendTemplate), null, inherits: true);

        /// <summary>
        /// The legend font family property
        /// </summary>
        public static readonly AvaloniaProperty<A.Media.FontFamily> LegendFontFamilyProperty =
           AvaloniaProperty.Register<CartesianChart, A.Media.FontFamily>(
               nameof(LegendFontFamily), new A.Media.FontFamily("Arial"), inherits: true);

        /// <summary>
        /// The legend font size property
        /// </summary>
        public static readonly AvaloniaProperty<double> LegendFontSizeProperty =
            AvaloniaProperty.Register<CartesianChart, double>(nameof(LegendFontSize), 13d, inherits: true);

        /// <summary>
        /// The legend font weight property
        /// </summary>
        public static readonly AvaloniaProperty<FontWeight> LegendFontWeightProperty =
            AvaloniaProperty.Register<CartesianChart, FontWeight>(nameof(LegendFontWeight), FontWeight.Normal, inherits: true);

        /// <summary>
        /// The legend font style property
        /// </summary>
        public static readonly AvaloniaProperty<A.Media.FontStyle> LegendFontStyleProperty =
            AvaloniaProperty.Register<CartesianChart, A.Media.FontStyle>(
                nameof(LegendFontStyle), A.Media.FontStyle.Normal, inherits: true);

        /// <summary>
        /// The legend text brush property
        /// </summary>
        public static readonly AvaloniaProperty<SolidColorBrush> LegendTextBrushProperty =
            AvaloniaProperty.Register<CartesianChart, SolidColorBrush>(
                nameof(LegendTextBrush), new SolidColorBrush(new A.Media.Color(255, 35, 35, 35)), inherits: true);

        /// <summary>
        /// The legend background property
        /// </summary>
        public static readonly AvaloniaProperty<IBrush> LegendBackgroundProperty =
            AvaloniaProperty.Register<CartesianChart, IBrush>(nameof(LegendBackground),
                new SolidColorBrush(new A.Media.Color(255, 250, 250, 250)), inherits: true);

        #endregion

        #region properties 

        /// <inheritdoc cref="IChartView{TDrawingContext}.CoreCanvas" />
        public MotionCanvas<SkiaSharpDrawingContext> CoreCanvas
        {
            get
            {
                if (core == null) throw new Exception("core not found");
                return core.Canvas;
            }
        }

        CartesianChart<SkiaSharpDrawingContext> ICartesianChartView<SkiaSharpDrawingContext>.Core
        {
            get
            {
                if (core == null) throw new Exception("core not found");
                return (CartesianChart<SkiaSharpDrawingContext>)core;
            }
        }

        SizeF IChartView.ControlSize
        {
            get
            {
                return new SizeF
                {
                    Width = (float)Bounds.Width,
                    Height = (float)Bounds.Height
                };
            }
        }

        /// <inheritdoc cref="IChartView.DrawMargin" />
        public Margin? DrawMargin
        {
            get { return (Margin?)GetValue(DrawMarginProperty); }
            set { SetValue(DrawMarginProperty, value); }
        }

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.Series" />
        public IEnumerable<ISeries> Series
        {
            get { return (IEnumerable<ISeries>)GetValue(SeriesProperty); }
            set { SetValue(SeriesProperty, value); }
        }

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.XAxes" />
        public IEnumerable<IAxis> XAxes
        {
            get { return (IEnumerable<IAxis>)GetValue(XAxesProperty); }
            set { SetValue(XAxesProperty, value); }
        }

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.YAxes" />
        public IEnumerable<IAxis> YAxes
        {
            get { return (IEnumerable<IAxis>)GetValue(YAxesProperty); }
            set { SetValue(YAxesProperty, value); }
        }

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ZoomMode" />
        public ZoomAndPanMode ZoomMode
        {
            get { return (ZoomAndPanMode)GetValue(ZoomModeProperty); }
            set { SetValue(ZoomModeProperty, value); }
        }

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ZoomingSpeed" />
        public double ZoomingSpeed
        {
            get { return (double)GetValue(ZoomingSpeedProperty); }
            set { SetValue(ZoomingSpeedProperty, value); }
        }

        /// <inheritdoc cref="IChartView.AnimationsSpeed" />
        public TimeSpan AnimationsSpeed
        {
            get { return (TimeSpan)GetValue(AnimationsSpeedProperty); }
            set { SetValue(AnimationsSpeedProperty, value); }
        }

        /// <inheritdoc cref="IChartView.EasingFunction" />
        public Func<float, float> EasingFunction
        {
            get { return (Func<float, float>)GetValue(EasingFunctionProperty); }
            set { SetValue(AnimationsSpeedProperty, value); }
        }

        /// <inheritdoc cref="IChartView.TooltipPosition" />
        public TooltipPosition TooltipPosition
        {
            get { return (TooltipPosition)GetValue(TooltipPositionProperty); }
            set { SetValue(TooltipPositionProperty, value); }
        }

        /// <inheritdoc cref="IChartView.TooltipFindingStrategy" />
        public TooltipFindingStrategy TooltipFindingStrategy
        {
            get { return (TooltipFindingStrategy)GetValue(TooltipFindingStrategyProperty); }
            set { SetValue(TooltipFindingStrategyProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip data template.
        /// </summary>
        /// <value>
        /// The tool tip template.
        /// </value>
        public DataTemplate TooltipTemplate
        {
            get { return (DataTemplate)GetValue(TooltipTemplateProperty); }
            set { SetValue(TooltipTemplateProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip default font family.
        /// </summary>
        /// <value>
        /// The tool tip font family.
        /// </value>
        public A.Media.FontFamily TooltipFontFamily
        {
            get { return (A.Media.FontFamily)GetValue(TooltipFontFamilyProperty); }
            set { SetValue(TooltipFontFamilyProperty, value); }
        }

        /// <summary>
        /// Gets or sets the default size of the tool tip font.
        /// </summary>
        /// <value>
        /// The size of the tool tip font.
        /// </value>
        public double TooltipFontSize
        {
            get { return (double)GetValue(TooltipFontSizeProperty); }
            set { SetValue(TooltipFontSizeProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip default font weight.
        /// </summary>
        /// <value>
        /// The tool tip font weight.
        /// </value>
        public FontWeight TooltipFontWeight
        {
            get { return (FontWeight)GetValue(TooltipFontWeightProperty); }
            set { SetValue(TooltipFontWeightProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip default font style.
        /// </summary>
        /// <value>
        /// The tool tip font style.
        /// </value>
        public A.Media.FontStyle TooltipFontStyle
        {
            get { return (A.Media.FontStyle)GetValue(TooltipFontStyleProperty); }
            set { SetValue(TooltipFontStyleProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip default text brush.
        /// </summary>
        /// <value>
        /// The tool tip text brush.
        /// </value>
        public SolidColorBrush TooltipTextBrush
        {
            get { return (SolidColorBrush)GetValue(TooltipTextBrushProperty); }
            set { SetValue(TooltipTextBrushProperty, value); }
        }

        /// <summary>
        /// Gets or sets the tool tip default background.
        /// </summary>
        /// <value>
        /// The tool tip background.
        /// </value>
        public IBrush TooltipBackground
        {
            get { return (IBrush)GetValue(TooltipBackgroundProperty); }
            set { SetValue(TooltipBackgroundProperty, value); }
        }

        /// <inheritdoc cref="IChartView{TDrawingContext}.Tooltip" />
        public IChartTooltip<SkiaSharpDrawingContext>? Tooltip => tooltip;

        /// <inheritdoc cref="IChartView.LegendPosition" />
        public LegendPosition LegendPosition
        {
            get { return (LegendPosition)GetValue(LegendPositionProperty); }
            set { SetValue(LegendPositionProperty, value); }
        }

        /// <inheritdoc cref="IChartView.LegendOrientation" />
        public LegendOrientation LegendOrientation
        {
            get { return (LegendOrientation)GetValue(LegendOrientationProperty); }
            set { SetValue(LegendOrientationProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend template.
        /// </summary>
        /// <value>
        /// The legend template.
        /// </value>
        public DataTemplate LegendTemplate
        {
            get { return (DataTemplate)GetValue(LegendTemplateProperty); }
            set { SetValue(LegendTemplateProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend default font family.
        /// </summary>
        /// <value>
        /// The legend font family.
        /// </value>
        public A.Media.FontFamily LegendFontFamily
        {
            get { return (A.Media.FontFamily)GetValue(LegendFontFamilyProperty); }
            set { SetValue(LegendFontFamilyProperty, value); }
        }

        /// <summary>
        /// Gets or sets the size of the legend default font.
        /// </summary>
        /// <value>
        /// The size of the legend font.
        /// </value>
        public double LegendFontSize
        {
            get { return (double)GetValue(LegendFontSizeProperty); }
            set { SetValue(LegendFontSizeProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend defailt font weight.
        /// </summary>
        /// <value>
        /// The legend font weight.
        /// </value>
        public FontWeight LegendFontWeight
        {
            get { return (FontWeight)GetValue(LegendFontWeightProperty); }
            set { SetValue(LegendFontWeightProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend default font style.
        /// </summary>
        /// <value>
        /// The legend font style.
        /// </value>
        public A.Media.FontStyle LegendFontStyle
        {
            get { return (A.Media.FontStyle)GetValue(LegendFontStyleProperty); }
            set { SetValue(LegendFontStyleProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend default text brush.
        /// </summary>
        /// <value>
        /// The legend text brush.
        /// </value>
        public SolidColorBrush LegendTextBrush
        {
            get { return (SolidColorBrush)GetValue(LegendTextBrushProperty); }
            set { SetValue(LegendTextBrushProperty, value); }
        }

        /// <summary>
        /// Gets or sets the legend default background.
        /// </summary>
        /// <value>
        /// The legend background.
        /// </value>
        public IBrush LegendBackground
        {
            get { return (IBrush)GetValue(LegendBackgroundProperty); }
            set { SetValue(LegendBackgroundProperty, value); }
        }

        /// <inheritdoc cref="IChartView{TDrawingContext}.Legend" />
        public IChartLegend<SkiaSharpDrawingContext>? Legend => legend;

        /// <inheritdoc cref="IChartView{TDrawingContext}.PointStates" />
        public PointStatesDictionary<SkiaSharpDrawingContext> PointStates { get; set; } = new();

        /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ScaleUIPoint(PointF, int, int)" />
        public PointF ScaleUIPoint(PointF point, int xAxisIndex = 0, int yAxisIndex = 0)
        {
            if (core == null) throw new Exception("core not found");
            var cartesianCore = (CartesianChart<SkiaSharpDrawingContext>)core;
            return cartesianCore.ScaleUIPoint(point, xAxisIndex, yAxisIndex);
        }

        #endregion

        /// <summary>
        /// Initializes the core.
        /// </summary>
        /// <returns></returns>
        protected void InitializeCore()
        {
            var canvas = this.FindControl<MotionCanvas>("canvas");
            core = new CartesianChart<SkiaSharpDrawingContext>(this, LiveChartsSkiaSharp.DefaultPlatformBuilder, canvas.CanvasCore);
            legend = this.FindControl<DefaultLegend>("legend");
            tooltip = this.FindControl<DefaultTooltip>("tooltip");
            Dispatcher.UIThread.InvokeAsync(() => core.Update(), DispatcherPriority.Background);
        }

        /// <inheritdoc cref="OnPropertyChanged{T}(AvaloniaPropertyChangedEventArgs{T})" />
        protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
        {
            base.OnPropertyChanged(change);

            if (core == null) return;

            if (change.Property.Name == nameof(Series))
            {
                seriesObserver.Dispose((IEnumerable<ISeries>)change.OldValue.Value);
                seriesObserver.Initialize((IEnumerable<ISeries>)change.NewValue.Value);
            }

            if (change.Property.Name == nameof(XAxes))
            {
                xObserver.Dispose((IEnumerable<IAxis>)change.OldValue.Value);
                xObserver.Initialize((IEnumerable<IAxis>)change.NewValue.Value);
            }

            if (change.Property.Name == nameof(YAxes))
            {
                yObserver.Dispose((IEnumerable<IAxis>)change.OldValue.Value);
                yObserver.Initialize((IEnumerable<IAxis>)change.NewValue.Value);
            }

            Dispatcher.UIThread.InvokeAsync(() => core.Update(), DispatcherPriority.Background);
        }

        private void MouseMoveThrottlerUnlocked()
        {
            if (core == null || tooltip == null || TooltipPosition == TooltipPosition.Hidden) return;
            tooltip.Show(core.FindPointsNearTo(mousePosition), core);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnDeepCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (core == null) return;

            Dispatcher.UIThread.InvokeAsync(() => core.Update(), DispatcherPriority.Background);
        }

        private void OnDeepCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (core == null) return;

            Dispatcher.UIThread.InvokeAsync(() => core.Update(), DispatcherPriority.Background);
        }

        private void CartesianChart_PointerWheelChanged(object? sender, global::Avalonia.Input.PointerWheelEventArgs e)
        {
            if (core == null) return;

            var c = (CartesianChart<SkiaSharpDrawingContext>)core;
            var p = e.GetPosition(this);

            c.Zoom(new PointF((float)p.X, (float)p.Y), e.Delta.Y > 0 ? ZoomDirection.ZoomIn : ZoomDirection.ZoomOut);
        }

        private void CartesianChart_PointerPressed(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            // only IClassicDesktopStyleApplicationLifetime supported for now.
            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

            isPanning = true;
            previous = e.GetPosition(this);

            foreach (var w in desktop.Windows) w.PointerReleased += Window_PointerReleased;
        }

        private void CartesianChart_PointerMoved(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            mousePosition = new PointF((float)p.X, (float)p.Y);
            mouseMoveThrottler.Call();

            if (!isPanning || previous == null) return;

            current = e.GetPosition(this);
            panningThrottler.Call();
        }

        private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

            if (!isPanning) return;
            isPanning = false;
            previous = null;

            foreach (var w in desktop.Windows) w.PointerReleased -= Window_PointerReleased;
        }

        private void DoPan()
        {
            if (previous == null || current == null || core == null) return;

            var c = (CartesianChart<SkiaSharpDrawingContext>)core;

            c.Pan(
                new PointF(
                (float)(current.Value.X - previous.Value.X),
                (float)(current.Value.Y - previous.Value.Y)));

            previous = new A.Point(current.Value.X, current.Value.Y);
        }
    }
}
