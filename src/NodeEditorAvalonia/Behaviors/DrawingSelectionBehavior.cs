﻿using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using NodeEditor.Controls;
using NodeEditor.Model;

namespace NodeEditor.Behaviors;

public class DrawingSelectionBehavior : Behavior<ItemsControl>
{
    public static readonly StyledProperty<Control?> InputSourceProperty = 
        AvaloniaProperty.Register<DrawingSelectionBehavior, Control?>(nameof(InputSource));

    public static readonly StyledProperty<Canvas?> AdornerCanvasProperty = 
        AvaloniaProperty.Register<DrawingSelectionBehavior, Canvas?>(nameof(AdornerCanvas));

    public static readonly StyledProperty<bool> EnableSnapProperty = 
        AvaloniaProperty.Register<DrawingSelectionBehavior, bool>(nameof(EnableSnap));

    public static readonly StyledProperty<double> SnapXProperty = 
        AvaloniaProperty.Register<DrawingSelectionBehavior, double>(nameof(SnapX), 1.0);

    public static readonly StyledProperty<double> SnapYProperty = 
        AvaloniaProperty.Register<DrawingSelectionBehavior, double>(nameof(SnapY), 1.0);

    private IDisposable? _isEditModeDisposable;
    private IDisposable? _dataContextDisposable;
    private INotifyPropertyChanged? _drawingNodePropertyChanged;
    private IDrawingNode? _drawingNode;
    private SelectionAdorner? _selectionAdorner;
    private SelectedAdorner? _selectedAdorner;
    private bool _dragSelectedItems;
    private Point _start;
    private Rect _selectedRect;
    private Control? _inputSource;

    public Control? InputSource
    {
        get => GetValue(InputSourceProperty);
        set => SetValue(InputSourceProperty, value);
    }

    public Canvas? AdornerCanvas
    {
        get => GetValue(AdornerCanvasProperty);
        set => SetValue(AdornerCanvasProperty, value);
    }

    public bool EnableSnap
    {
        get => GetValue(EnableSnapProperty);
        set => SetValue(EnableSnapProperty, value);
    }

    public double SnapX
    {
        get => GetValue(SnapXProperty);
        set => SetValue(SnapXProperty, value);
    }

    public double SnapY
    {
        get => GetValue(SnapYProperty);
        set => SetValue(SnapYProperty, value);
    }

    protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == InputSourceProperty)
        {
            DeInitialize();

            if (AssociatedObject is { } && InputSource is { })
            {
                Initialize();
            }
        }
    }

    private void Initialize()
    {
        if (AssociatedObject is null || InputSource is null)
        {
            return;
        }

        _inputSource = InputSource;

        _inputSource.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        _inputSource.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        _inputSource.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);

        _isEditModeDisposable = AssociatedObject.GetObservable(DrawingNode.IsEditModeProperty)
            .Subscribe(x =>
            {
                if (x == false)
                {
                    RemoveSelection();
                    RemoveSelected();
                }
            });

        _dataContextDisposable = AssociatedObject
            .GetObservable(StyledElement.DataContextProperty)
            .Subscribe(x =>
            {
                if (x is IDrawingNode drawingNode)
                {
                    if (_drawingNode == drawingNode)
                    {
                        if (_drawingNodePropertyChanged != null)
                        {
                            _drawingNodePropertyChanged.PropertyChanged -= DrawingNode_PropertyChanged;
                        }
                    }

                    RemoveSelection();
                    RemoveSelected();

                    _drawingNode = drawingNode;

                    if (_drawingNode is INotifyPropertyChanged notifyPropertyChanged)
                    {
                        _drawingNodePropertyChanged = notifyPropertyChanged;
                        _drawingNodePropertyChanged.PropertyChanged += DrawingNode_PropertyChanged;
                    }
                }
                else
                {
                    RemoveSelection();
                    RemoveSelected();
                }
            });
    }

    private void DeInitialize()
    {
        if (_inputSource is { })
        {
            _inputSource.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            _inputSource.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            _inputSource.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            _inputSource = null;
        }

        if (_drawingNodePropertyChanged is { })
        {
            _drawingNodePropertyChanged.PropertyChanged -= DrawingNode_PropertyChanged;
            _drawingNodePropertyChanged = null;
        }

        _isEditModeDisposable?.Dispose();
        _isEditModeDisposable = null;

        _dataContextDisposable?.Dispose();
        _dataContextDisposable = null;
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is { } && InputSource is { })
        {
            Initialize();
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        DeInitialize();
    }

    private double Snap(double value, double snap)
    {
        if (snap == 0.0)
        {
            return value;
        }
        var c = value % snap;
        var r = c >= snap / 2.0 ? value + snap - c : value - c;
        return r;
    }

    private Point Snap(Point point)
    {
        if (EnableSnap)
        {
            var pointX = Snap(point.X, SnapX);
            var pointY = Snap(point.Y, SnapY);
            return new Point(pointX, pointY);
        }

        return point;
    }

    private void DrawingNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (AssociatedObject?.DataContext is not IDrawingNode)
        {
            return;
        }

        if (e.PropertyName == nameof(IDrawingNode.SelectedNodes) || e.PropertyName == nameof(IDrawingNode.SelectedConnectors))
        {
            if (_drawingNode is { })
            {
                if (_drawingNode.SelectedNodes is { Count: > 0 } || _drawingNode.SelectedConnectors is { Count: > 0 })
                {
                    _selectedRect = HitTestHelper.CalculateSelectedRect(AssociatedObject);

                    if (_selectedAdorner is { })
                    {
                        RemoveSelected();
                    }

                    if (!_selectedRect.IsEmpty && _selectedAdorner is null)
                    {
                        AddSelected(AssociatedObject, _selectedRect);
                    }
                }
                else
                {
                    RemoveSelected();
                }
            }
        }
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject?.DataContext is not IDrawingNode drawingNode)
        {
            return;
        }

        if (e.Source is Control { DataContext: IPin })
        {
            return;
        }

        if (!AssociatedObject.GetValue(DrawingNode.IsEditModeProperty))
        {
            return;
        }

        var position = e.GetPosition(AssociatedObject);

        position = Snap(position);

        if (!drawingNode.CanSelectNodes() && !drawingNode.CanSelectConnectors())
        {
            return;
        }

        if (e.GetCurrentPoint(_inputSource).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(_inputSource);

            _dragSelectedItems = false;

            var pointerHitTestRect = new Rect(position.X - 1, position.Y - 1, 3, 3);

            if (drawingNode.SelectedNodes is { Count: > 0 } || drawingNode.SelectedConnectors is { Count: > 0 })
            {
                if (_selectedRect.Contains(position))
                {
                    _dragSelectedItems = true;
                    _start = position;
                    e.Handled = true;
                }
                else
                {
                    HitTestHelper.FindSelectedNodes(AssociatedObject, pointerHitTestRect);

                    if (drawingNode.SelectedNodes is { Count: > 0 } || drawingNode.SelectedConnectors is { Count: > 0 })
                    {
                        _dragSelectedItems = true;
                        _start = position;
                        e.Handled = true;
                    }
                    else
                    {
                        drawingNode.SelectedNodes = null;
                        drawingNode.SelectedConnectors = null;
                        RemoveSelected();
                    }
                }
            }
            else
            {
                HitTestHelper.FindSelectedNodes(AssociatedObject, pointerHitTestRect);

                if (drawingNode.SelectedNodes is { Count: > 0 } || drawingNode.SelectedConnectors is { Count: > 0 })
                {
                    _dragSelectedItems = true;
                    _start = position;
                    e.Handled = true;
                } 
            }

            if (!_dragSelectedItems)
            {
                drawingNode.SelectedNodes = null;
                drawingNode.SelectedConnectors = null;
                RemoveSelected();

                if (e.Source is Control { DataContext: not IDrawingNode })
                {
                    return;
                }

                AddSelection(AssociatedObject, position.X, position.Y);
            }
        }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (AssociatedObject?.DataContext is not IDrawingNode)
        {
            return;
        }

        _dragSelectedItems = false;

        if (e.Source is Control { DataContext: not IDrawingNode })
        {
            return;
        }

        if (_selectionAdorner is { })
        {
            HitTestHelper.FindSelectedNodes(AssociatedObject, _selectionAdorner.GetRect());
        }

        RemoveSelection();
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (AssociatedObject?.DataContext is not IDrawingNode)
        {
            return;
        }

        var position = e.GetPosition(AssociatedObject);

        if (_dragSelectedItems)
        {
            Move(position);

            e.Handled = true;
        }
        else
        {
            if (e.Source is Control { DataContext: not IDrawingNode })
            {
                return;
            }

            UpdateSelection(position.X, position.Y);
        }
    }

    private void Move(Point position)
    {
        if (AssociatedObject?.DataContext is not IDrawingNode drawingNode)
        {
            return;
        }

        if (drawingNode.SelectedNodes is not { Count: > 0 } || drawingNode.Nodes is not { Count: > 0 })
        {
            return;
        }

        position = Snap(position);

        var deltaX = position.X - _start.X;
        var deltaY = position.Y - _start.Y;
        _start = position;

        var selectedRect = HitTestHelper.CalculateSelectedRect(AssociatedObject);

        _selectedRect = selectedRect;

        foreach (var node in drawingNode.SelectedNodes)
        {
            if (node.CanMove())
            {
                node.Move(deltaX, deltaY);
            }
        }

        UpdateSelected(selectedRect);
    }

    private void AddSelection(Control control, double x, double y)
    {
        var layer = AdornerCanvas;
        if (layer is null)
        {
            return;
        }

        _selectionAdorner = new SelectionAdorner
        {
            IsHitTestVisible = false,
            TopLeft = new Point(x, y),
            BottomRight = new Point(x, y)
        };

        ((ISetLogicalParent) _selectionAdorner).SetParent(control);
        layer.Children.Add(_selectionAdorner);
        
        _selectionAdorner.Invalidate();
    }

    private void RemoveSelection()
    {
        var layer = AdornerCanvas;
        if (layer is null || _selectionAdorner is null)
        {
            return;
        }

        layer.Children.Remove(_selectionAdorner);
        ((ISetLogicalParent) _selectionAdorner).SetParent(null);
        _selectionAdorner = null;
    }

    private void UpdateSelection(double x, double y)
    {
        if (_selectionAdorner is { } selection)
        {
            selection.BottomRight = new Point(x, y);
            selection.Invalidate();
        }
    }

    private void AddSelected(Control control, Rect rect)
    {
        var layer = AdornerCanvas;
        if (layer is null)
        {
            return;
        }

        _selectedAdorner = new SelectedAdorner
        {
            IsHitTestVisible = true,
            Rect = rect,
            EnableResizing = false,
            EnableDragging = false
        };

        ((ISetLogicalParent) _selectedAdorner).SetParent(control);
        layer.Children.Add(_selectedAdorner);

        _selectedAdorner.Invalidate();
    }

    private void RemoveSelected()
    {
        var layer = AdornerCanvas;
        if (layer is null || _selectedAdorner is null)
        {
            return;
        }

        layer.Children.Remove(_selectedAdorner);
        ((ISetLogicalParent) _selectedAdorner).SetParent(null);
        _selectedAdorner = null;
    }

    private void UpdateSelected(Rect rect)
    {
        if (_selectedAdorner is { } selected)
        {
            selected.Rect = rect;
            selected.Invalidate();
        }
    }
}
