using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace happy_agent_coding.Views;

public sealed class ObjectTileWrapHost : WrapPanel
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ObjectTileWrapHost, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<ObjectTileWrapHost, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<Control?> TrailingContentProperty =
        AvaloniaProperty.Register<ObjectTileWrapHost, Control?>(nameof(TrailingContent));

    private INotifyCollectionChanged? _subscribed;

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public Control? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            Subscribe(change.GetNewValue<IEnumerable?>());
            Rebuild();
        }
        else if (change.Property == ItemTemplateProperty || change.Property == TrailingContentProperty)
        {
            Rebuild();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(ItemsSource);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe(IEnumerable? source)
    {
        if (_subscribed is not null)
        {
            _subscribed.CollectionChanged -= OnItemsChanged;
            _subscribed = null;
        }

        if (source is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += OnItemsChanged;
            _subscribed = notify;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var trailing = TrailingContent;
        Children.Clear();

        if (ItemsSource is not null && ItemTemplate is not null)
        {
            foreach (var item in ItemsSource)
            {
                if (item is null)
                    continue;

                var visual = ItemTemplate.Build(item);
                if (visual is null)
                    continue;

                visual.DataContext = item;
                Children.Add(visual);
            }
        }

        if (trailing is not null)
            Children.Add(trailing);
    }
}
