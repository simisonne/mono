using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using mono.Models;
using mono.ViewModels;

namespace mono.Views;

public partial class PlaylistDock : UserControl
{
    private bool _isSyncingSelection = false;
    private Point _dragStartPoint;
    private TrackItem? _draggedItem;
    private bool _isDragging = false;

    private DragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private int _insertionIndex = -1;
    private InsertionAdorner? _insertionAdorner;
    private bool _shiftDrag = false;

    public PlaylistDock()
    {
        InitializeComponent();

        TrackList.PreviewMouseRightButtonDown += TrackList_PreviewMouseRightButtonDown;

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            oldVm.Queue.CollectionChanged -= OnQueueCollectionChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            newVm.Queue.CollectionChanged += OnQueueCollectionChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentQueueIndex))
        {
            Dispatcher.BeginInvoke(() =>
            {
                var vm = DataContext as MainViewModel;
                if (vm != null && vm.CurrentQueueIndex >= 0 && vm.CurrentQueueIndex < TrackList.Items.Count)
                {
                    _isSyncingSelection = true;
                    TrackList.SelectedIndex = vm.CurrentQueueIndex;
                    _isSyncingSelection = false;
                }
            });
        }
    }

    private void OnQueueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TrackList.Items.Refresh();
            });
        }
    }

    private void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        if (_isDragging) return;
        if (_shiftDrag) return;
        if (TrackList.SelectedIndex < 0) return;
        App.ViewModel.PlayTrackAtIndex(TrackList.SelectedIndex);
    }

    private void TrackList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItem = null;
        _shiftDrag = Keyboard.Modifiers == ModifierKeys.Shift;

        if (e.OriginalSource is DependencyObject d)
        {
            var lvi = FindAncestor<ListViewItem>(d);
            if (lvi != null)
            {
                var item = TrackList.ItemContainerGenerator.ItemFromContainer(lvi);
                if (item is TrackItem track)
                    _draggedItem = track;

                if (_shiftDrag)
                    e.Handled = true;
            }
        }
    }

    private void TrackList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _shiftDrag = false;
    }

    private void TrackList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
            return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            try
            {
                PrepareDragAdorner(e);
                DragDrop.DoDragDrop(TrackList, _draggedItem, DragDropEffects.Move);
            }
            finally
            {
                RemoveDragAdorner();
                RemoveInsertionAdorner();
                _isDragging = false;
                _draggedItem = null;
                _shiftDrag = false;
            }
        }
    }

    private void PrepareDragAdorner(MouseEventArgs e)
    {
        var lvi = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (lvi == null) return;

        var brush = new VisualBrush(lvi)
        {
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.None,
            Opacity = 0.75
        };

        var root = (FrameworkElement)Application.Current.MainWindow.Content;
        Point pos = e.GetPosition(root);

        _dragAdorner = new DragAdorner(root, lvi.RenderSize, brush)
        {
            OffsetX = pos.X - _dragStartPoint.X,
            OffsetY = pos.Y - _dragStartPoint.Y
        };

        _adornerLayer = AdornerLayer.GetAdornerLayer(root);
        _adornerLayer?.Add(_dragAdorner);
    }

    private void RemoveDragAdorner()
    {
        if (_dragAdorner != null && _adornerLayer != null)
        {
            _adornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
        }
    }

    private void TrackList_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedItem == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        Point pos = e.GetPosition((FrameworkElement)Application.Current.MainWindow.Content);
        if (_dragAdorner != null)
        {
            _dragAdorner.Left = pos.X - _dragAdorner.OffsetX;
            _dragAdorner.Top = pos.Y - _dragAdorner.OffsetY;
            _adornerLayer?.Update();
        }

        UpdateInsertionIndex(e);
    }

    private void UpdateInsertionIndex(DragEventArgs e)
    {
        int newInsertIndex;

        Point posInList = e.GetPosition(TrackList);
        var scrollViewer = FindChild<ScrollViewer>(TrackList);
        if (scrollViewer != null)
        {
            if (posInList.Y <= 0)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - 8);
                newInsertIndex = 0;
                ShowInsertionAdorner(newInsertIndex);
                return;
            }
            if (posInList.Y >= TrackList.ActualHeight)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 8);
                newInsertIndex = TrackList.Items.Count;
                ShowInsertionAdorner(newInsertIndex);
                return;
            }
        }

        var hitResult = TrackList.InputHitTest(e.GetPosition(TrackList)) as DependencyObject;
        var lvi = hitResult != null ? FindAncestor<ListViewItem>(hitResult) : null;

        if (lvi != null)
        {
            int idx = TrackList.ItemContainerGenerator.IndexFromContainer(lvi);
            Point posInItem = e.GetPosition(lvi);
            bool insertBefore = posInItem.Y < lvi.ActualHeight / 2.0;
            newInsertIndex = insertBefore ? idx : idx + 1;
        }
        else
        {
            newInsertIndex = TrackList.Items.Count;
        }

        ShowInsertionAdorner(newInsertIndex);
    }

    private void ShowInsertionAdorner(int insertIndex)
    {
        if (insertIndex == _insertionIndex && _insertionAdorner != null)
            return;

        _insertionIndex = insertIndex;
        RemoveInsertionAdorner();

        var root = (FrameworkElement)Application.Current.MainWindow.Content;
        _adornerLayer = AdornerLayer.GetAdornerLayer(root);

        if (_adornerLayer == null) return;

        double y;
        if (insertIndex < TrackList.Items.Count)
        {
            var container = (FrameworkElement?)TrackList.ItemContainerGenerator.ContainerFromIndex(insertIndex);
            if (container == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(root);
                return;
            }
            Point p = container.TransformToAncestor(root).Transform(new Point(0, 0));
            y = p.Y;
        }
        else if (TrackList.Items.Count > 0)
        {
            var lastContainer = (FrameworkElement?)TrackList.ItemContainerGenerator.ContainerFromIndex(TrackList.Items.Count - 1);
            if (lastContainer == null) return;
            Point p = lastContainer.TransformToAncestor(root).Transform(new Point(0, 0));
            y = p.Y + lastContainer.ActualHeight;
        }
        else
        {
            return;
        }

        Point listOrigin = TrackList.TransformToAncestor(root).Transform(new Point(0, 0));
        double left = listOrigin.X + 4;
        double width = TrackList.ActualWidth - 8;

        _insertionAdorner = new InsertionAdorner(root, left, y, width);
        _adornerLayer.Add(_insertionAdorner);
    }

    private void RemoveInsertionAdorner()
    {
        if (_insertionAdorner != null && _adornerLayer != null)
        {
            _adornerLayer.Remove(_insertionAdorner);
            _insertionAdorner = null;
        }
    }

    private void TrackList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedItem == null) return;

        int oldIndex = TrackList.Items.IndexOf(_draggedItem);
        if (oldIndex < 0) return;

        int newIndex = _insertionIndex;
        if (newIndex < 0 || newIndex > TrackList.Items.Count) return;
        if (oldIndex == newIndex) return;

        if (newIndex > oldIndex)
            newIndex--;

        if (oldIndex != newIndex && newIndex >= 0 && newIndex < TrackList.Items.Count)
        {
            var vm = DataContext as MainViewModel;
            vm?.MoveInQueue(oldIndex, newIndex);
        }
    }

    private void TrackList_DragLeave(object sender, DragEventArgs e)
    {
        RemoveInsertionAdorner();
    }

    private void TrackList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            var lvi = FindAncestor<ListViewItem>(d);
            if (lvi?.DataContext is TrackItem track)
            {
                TrackList.SelectedItem = track;
                var menu = new ContextMenu();

                var playItem = new MenuItem { Header = "Play" };
                playItem.Click += (_, _) =>
                {
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                    {
                        int index = vm.Queue.IndexOf(track);
                        if (index >= 0) vm.PlayTrackAtIndex(index);
                    }
                };

                var removeItem = new MenuItem { Header = "Remove from list" };
                removeItem.Click += (_, _) => (DataContext as MainViewModel)?.RemoveTrack(track);

                var showItem = new MenuItem { Header = "Show in Folder" };
                showItem.Click += (_, _) => (DataContext as MainViewModel)?.ShowInFolder(track);

                menu.Items.Add(playItem);
                menu.Items.Add(removeItem);
                menu.Items.Add(new Separator());
                menu.Items.Add(showItem);

                menu.IsOpen = true;
                e.Handled = true;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private class DragAdorner : Adorner
    {
        private readonly VisualBrush _brush;
        private readonly Rect _rect;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }

        public DragAdorner(UIElement adorned, Size size, VisualBrush brush)
            : base(adorned)
        {
            _brush = brush;
            _rect = new Rect(size);
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(_brush, null, new Rect(Left, Top, _rect.Width, _rect.Height));
        }
    }

    private class InsertionAdorner : Adorner
    {
        private readonly double _x;
        private readonly double _y;
        private readonly double _width;
        private static readonly Pen _pen = new(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), 2)
        {
            DashCap = PenLineCap.Round
        };

        static InsertionAdorner()
        {
            _pen.Freeze();
        }

        public InsertionAdorner(UIElement adorned, double x, double y, double width)
            : base(adorned)
        {
            _x = x;
            _y = y;
            _width = width;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawLine(_pen, new Point(_x, _y), new Point(_x + _width, _y));

            double triSize = 4;
            var fill = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
            fill.Freeze();

            dc.DrawEllipse(fill, null, new Point(_x, _y), triSize, triSize);
            dc.DrawEllipse(fill, null, new Point(_x + _width, _y), triSize, triSize);
        }
    }
}
