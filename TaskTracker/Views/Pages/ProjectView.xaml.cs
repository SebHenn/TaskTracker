using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskTracker.Controls;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Helpers;
using TaskTracker.ViewModels.Pages;

namespace TaskTracker.Views.Pages
{
    /// <summary>
    /// Interaction logic for ProjectView.xaml
    /// </summary>
    public partial class ProjectView : UserControl
    {
        private const double AutoScrollZone = 32;
        private const double AutoScrollStep = 12;

        private Point _dragStart;
        private Vector _grabOffset;
        private TaskModel? _pressedTask;
        private bool _dragStarted;

        // Live drag state, all torn down in EndDrag.
        private DragAdorner? _ghost;
        private AdornerLayer? _ghostLayer;
        private Point _originInBoard;
        private FrameworkElement? _dimmedSource;
        private ColumnLaneViewModel? _hoverLane;
        private InsertionAdorner? _insertion;
        private AdornerLayer? _insertionLayer;
        private ListBox? _insertionHost;
        private ListBox? _dropList;
        private Guid _dropTaskId;

        private DispatcherTimer? _autoScrollTimer;
        private ScrollViewer? _autoScroller;
        private double _autoScrollStep;

        public ProjectView()
        {
            InitializeComponent();
        }

        // ---- Press, then maybe drag --------------------------------------------------

        // A press arms a potential drag; actual dragging starts only after the
        // mouse moves past the system threshold, so a plain click falls through
        // to mouse-up and opens the detail panel instead.
        private void OnTaskPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressedTask = (sender as FrameworkElement)?.DataContext as TaskModel;
            _dragStart = e.GetPosition(this);
            _dragStarted = false;

            // Where inside the card the grab happened. Held constant for the whole
            // drag so the ghost stays under the cursor at that same spot, rather than
            // snapping its corner to the pointer.
            _grabOffset = sender is FrameworkElement card
                ? (Vector)e.GetPosition(card)
                : new Vector();
        }

        private void OnTaskPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedTask == null || _dragStarted || e.LeftButton != MouseButtonState.Pressed)
                return;

            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (sender is not FrameworkElement source)
                return;

            _dragStarted = true;
            var task = _pressedTask;
            _pressedTask = null;

            BeginDrag(source);
            DragDropEffects result;
            try
            {
                // Per-drag so the handler's lifetime is exactly the drag's, even though
                // GiveFeedback would bubble to the view either way.
                DragDrop.AddGiveFeedbackHandler(source, OnGiveFeedback);
                result = DragDrop.DoDragDrop(source, task.Id, DragDropEffects.Move);
            }
            finally
            {
                DragDrop.RemoveGiveFeedbackHandler(source, OnGiveFeedback);
            }

            EndDrag(result);
        }

        private void OnTaskPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pressedTask != null && !_dragStarted &&
                (sender as FrameworkElement)?.DataContext == _pressedTask &&
                DataContext is ProjectViewModel viewModel)
            {
                viewModel.OnOpenTaskDetail(_pressedTask);
            }
            _pressedTask = null;
            _dragStarted = false;
        }

        // ---- The ghost ---------------------------------------------------------------

        private void BeginDrag(FrameworkElement source)
        {
            _dropList = null;
            _dropTaskId = Guid.Empty;

            _ghostLayer = AdornerLayer.GetAdornerLayer(BoardRoot);
            if (_ghostLayer == null)
                return; // no adorner layer: the drag still works, just without a ghost

            _originInBoard = source.TranslatePoint(new Point(0, 0), BoardRoot);
            _ghost = new DragAdorner(BoardRoot, source);
            _ghost.SetPosition(_originInBoard);
            _ghostLayer.Add(_ghost);

            // The card is being carried, so the slot it left reads as vacated.
            //
            // Set on the container, which recycling can hand to another task: auto-
            // scrolling the source lane far enough to virtualize this row away would
            // briefly dim whichever card inherits it. Cosmetic, and EndDrag clears it
            // either way — worth it for the vacated slot the rest of the time.
            source.Opacity = 0.4;
            _dimmedSource = source;
        }

        /// <summary>
        /// Fires for every pointer move for the length of the drag — including over
        /// places that are not drop targets, where DragOver never runs. That makes it
        /// the only hook that can keep the ghost glued to the cursor the whole time.
        /// </summary>
        private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            // The stock drag cursors — the "no drop" circle above all — fight the ghost
            // for the user's attention and say nothing it doesn't already say.
            e.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Arrow);
            e.Handled = true;

            if (_ghost == null)
                return;

            var point = BoardRoot.PointFromScreen(NativeMouse.ScreenPosition());
            _ghost.SetPosition(point - _grabOffset);

            // Dragged clear of the board entirely (over the sidebar, the header, off the
            // window): no lane DragOver will fire to correct a stale highlight, so drop
            // it here.
            if (point.X < 0 || point.Y < 0 || point.X > BoardRoot.ActualWidth || point.Y > BoardRoot.ActualHeight)
                ClearHover();
        }

        private void EndDrag(DragDropEffects result)
        {
            ClearHover();

            if (_dimmedSource != null)
            {
                _dimmedSource.Opacity = 1;
                _dimmedSource = null;
            }

            var ghost = _ghost;
            var list = _dropList;
            var taskId = _dropTaskId;
            _ghost = null;
            _dropList = null;
            _dropTaskId = Guid.Empty;

            if (ghost == null)
                return;

            if (result == DragDropEffects.Move && list != null && taskId != Guid.Empty)
            {
                // The lanes re-projected during the drop, so the destination slot only
                // has real bounds once layout has caught up.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    var slot = SlotOrigin(list, taskId);
                    if (slot == null)
                        ghost.FadeOut(() => RemoveGhost(ghost));
                    else
                        ghost.SettleAt(slot.Value, () => RemoveGhost(ghost));
                });
                return;
            }

            // Dropped outside every lane, or cancelled with Escape. Nothing moved, so
            // the card visibly goes back where it came from.
            ghost.SettleAt(_originInBoard, () => RemoveGhost(ghost));
        }

        private void RemoveGhost(DragAdorner ghost)
        {
            _ghostLayer?.Remove(ghost);
        }

        /// <summary>Top-left of a task's realized row, in board coordinates.</summary>
        private Point? SlotOrigin(ListBox list, Guid taskId)
        {
            for (var i = 0; i < list.Items.Count; i++)
            {
                if (list.Items[i] is not TaskModel task || task.Id != taskId)
                    continue;
                // Virtualized away (the lane scrolled past it): there is no slot on
                // screen to snap into.
                return list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container && container.IsVisible
                    ? container.TranslatePoint(new Point(0, 0), BoardRoot)
                    : null;
            }
            return null;
        }

        // ---- Drop targets -----------------------------------------------------------

        private void OnDragOverLane(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (sender is not FrameworkElement laneRoot ||
                laneRoot.DataContext is not ColumnLaneViewModel lane ||
                !e.Data.GetDataPresent(typeof(Guid)))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            SetHoverLane(lane);

            var list = FindDescendant<ListBox>(laneRoot);
            if (list == null)
                return;

            var point = e.GetPosition(list);
            ShowInsertion(list, ComputeDropSlot(list, lane, (Guid)e.Data.GetData(typeof(Guid))!, point).VisualIndex);
            UpdateAutoScroll(list, point);
        }

        /// <summary>
        /// The board behind the lanes. Reached only when the pointer is in the gutter
        /// between columns, because a lane marks its own DragOver handled — which is
        /// what tells "outside a column" apart from "over one".
        /// </summary>
        private void OnDragOverBoard(object sender, DragEventArgs e)
        {
            ClearHover();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDropLane(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ProjectViewModel viewModel)
                return;
            if (sender is not FrameworkElement laneRoot || laneRoot.DataContext is not ColumnLaneViewModel lane)
                return;
            if (!e.Data.GetDataPresent(typeof(Guid)))
                return;

            var taskId = (Guid)e.Data.GetData(typeof(Guid))!;
            var list = FindDescendant<ListBox>(laneRoot);
            var insertIndex = list == null
                ? int.MaxValue
                : ComputeDropSlot(list, lane, taskId, e.GetPosition(list)).MoveIndex;

            viewModel.MoveTask(taskId, lane, insertIndex);

            // Handed to EndDrag once DoDragDrop unwinds, for the snap animation.
            _dropList = list;
            _dropTaskId = taskId;
        }

        /// <summary>
        /// Where a drop would land, in two frames of reference: the position in the
        /// lane as currently displayed (which still contains the dragged card, and is
        /// what the insertion line is drawn against), and the position in the lane
        /// without it (which is what <see cref="ProjectViewModel.MoveTask"/> works in).
        /// </summary>
        private readonly record struct DropSlot(int VisualIndex, int MoveIndex);

        /// <summary>
        /// Hit-tests the card under the pointer rather than walking the lane by index:
        /// now that the list virtualizes, off-screen rows have no container at all, so
        /// an index walk stops at the first unrealized row and drops the card in the
        /// wrong place as soon as a column is scrolled.
        /// </summary>
        private static DropSlot ComputeDropSlot(ListBox list, ColumnLaneViewModel lane, Guid draggedId, Point point)
        {
            var visualIndex = list.Items.Count; // empty space past the last card: append

            if (list.InputHitTest(point) is DependencyObject hit &&
                list.ContainerFromElement(hit) is FrameworkElement container)
            {
                var index = list.ItemContainerGenerator.IndexFromContainer(container);
                if (index >= 0)
                {
                    // Past the card's midpoint means "after it".
                    var midpoint = container.TranslatePoint(new Point(0, container.ActualHeight / 2), list).Y;
                    visualIndex = point.Y < midpoint ? index : index + 1;
                }
            }

            return new DropSlot(visualIndex, BoardDrop.ResolveMoveIndex(visualIndex, IndexOfTask(lane, draggedId)));
        }

        private static int IndexOfTask(ColumnLaneViewModel lane, Guid id)
        {
            for (var i = 0; i < lane.Tasks.Count; i++)
            {
                if (lane.Tasks[i].Id == id)
                    return i;
            }
            return -1;
        }

        // ---- Hover feedback ---------------------------------------------------------

        private void SetHoverLane(ColumnLaneViewModel? lane)
        {
            if (ReferenceEquals(_hoverLane, lane))
                return;

            if (_hoverLane != null)
                _hoverLane.IsDropTarget = false;
            _hoverLane = lane;
            if (_hoverLane != null)
                _hoverLane.IsDropTarget = true;

            _ghost?.SetOverDropTarget(lane != null);
        }

        private void ClearHover()
        {
            SetHoverLane(null);
            RemoveInsertion();
            StopAutoScroll();
        }

        private void ShowInsertion(ListBox list, int visualIndex)
        {
            if (!ReferenceEquals(_insertionHost, list))
            {
                RemoveInsertion();
                var layer = AdornerLayer.GetAdornerLayer(list);
                if (layer == null)
                    return;
                _insertion = new InsertionAdorner(list, TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue);
                layer.Add(_insertion);
                _insertionLayer = layer;
                _insertionHost = list;
            }

            var offset = InsertionOffset(list, visualIndex);
            if (offset == null)
                _insertion?.Hide();
            else
                _insertion?.ShowAt(offset.Value);
        }

        private void RemoveInsertion()
        {
            if (_insertion != null)
                _insertionLayer?.Remove(_insertion);
            _insertion = null;
            _insertionLayer = null;
            _insertionHost = null;
        }

        /// <summary>Top edge of the row at <paramref name="visualIndex"/>, or the bottom of the one before it.</summary>
        private static double? InsertionOffset(ListBox list, int visualIndex)
        {
            if (list.Items.Count == 0)
                return 6; // empty lane: a stub near the top, so the target still reads

            if (visualIndex < list.Items.Count &&
                list.ItemContainerGenerator.ContainerFromIndex(visualIndex) is FrameworkElement next)
                return next.TranslatePoint(new Point(0, 0), list).Y;

            var previousIndex = Math.Min(visualIndex, list.Items.Count) - 1;
            if (previousIndex >= 0 &&
                list.ItemContainerGenerator.ContainerFromIndex(previousIndex) is FrameworkElement previous)
                return previous.TranslatePoint(new Point(0, previous.ActualHeight), list).Y;

            return null;
        }

        // ---- Auto-scroll ------------------------------------------------------------

        private void UpdateAutoScroll(ListBox list, Point point)
        {
            var scroller = FindDescendant<ScrollViewer>(list);
            if (scroller == null || scroller.ScrollableHeight <= 0)
            {
                StopAutoScroll();
                return;
            }

            var step = point.Y < AutoScrollZone ? -AutoScrollStep
                : point.Y > list.ActualHeight - AutoScrollZone ? AutoScrollStep
                : 0;
            if (step == 0)
            {
                StopAutoScroll();
                return;
            }

            _autoScroller = scroller;
            _autoScrollStep = step;
            // On a timer, not per DragOver: those only fire while the pointer moves, and
            // holding still at the edge of a lane is exactly when scrolling is wanted.
            _autoScrollTimer ??= CreateAutoScrollTimer();
            _autoScrollTimer.Start();
        }

        private DispatcherTimer CreateAutoScrollTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (_, _) =>
            {
                if (_autoScroller == null)
                {
                    StopAutoScroll();
                    return;
                }
                _autoScroller.ScrollToVerticalOffset(_autoScroller.VerticalOffset + _autoScrollStep);
            };
            return timer;
        }

        private void StopAutoScroll()
        {
            _autoScrollTimer?.Stop();
            _autoScroller = null;
            _autoScrollStep = 0;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;
                if (FindDescendant<T>(child) is { } nested)
                    return nested;
            }
            return null;
        }
    }
}
