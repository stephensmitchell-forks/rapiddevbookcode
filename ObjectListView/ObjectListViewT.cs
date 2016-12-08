using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;

namespace JesseJohnston
{
  /// <summary>
  /// View of a list of objects of type T.
  /// </summary>
  /// <typeparam name="T"></typeparam>
  [Serializable]
  [DebuggerVisualizer(typeof (ObjectListViewVisualizer))]
  public class ObjectListView<T> : IBindingListView, IList<T>, ITypedList, IRaiseItemChangedEvents, ICancelAddNew, IDeserializationCallback
  {
    #region Types

    /// <summary>
    /// Argument type for the <seealso cref="ObjectListView{T}.AddingNew"/> event.
    /// </summary>
    public class AddingNewEventArgs : EventArgs
    {
      private T newItem;
      private bool set;

      /// <summary>
      /// Gets or sets the new object provided by the event handler (if any).
      /// </summary>
      /// <value>The new object.</value>
      public T NewObject
      {
        get { return newItem; }
        set
        {
          newItem = value;
          set = true;
        }
      }

      /// <summary>
      /// Gets a value indicating whether a new object was provided by an event handler.
      /// </summary>
      /// <value><b>true</b> if a new object was provided; otherwise, <b>false</b>.</value>
      public bool UserSetNewObject
      {
        get { return set; }
      }
    }

    private class ObjectListViewEnumerator : IEnumerator<T>
    {
      private int position = -1;
      private readonly ObjectListView<T> view;
      private readonly int version;

      public ObjectListViewEnumerator(ObjectListView<T> view)
      {
        if (view == null)
          throw new ArgumentNullException("view");
        this.view = view;
        version = view.version;
      }

      public T Current
      {
        get
        {
          if (version != view.version)
            throw new InvalidOperationException("The collection has been modified.");

          if (position > view.Count - 1)
            throw new InvalidOperationException("The enumerator is past the end of the collection.");

          if (position == -1)
            throw new InvalidOperationException("The enumerator is before the beginning of the collection.");

          return view[position];
        }
      }

      public bool MoveNext()
      {
        if (version != view.version)
          throw new InvalidOperationException("The collection has been modified.");

        if (position >= view.Count - 1)
          return false;
        else
        {
          position++;
          return true;
        }
      }

      public void Reset()
      {
        if (version != view.version)
          throw new InvalidOperationException("The collection has been modified.");
        position = -1;
      }

      object IEnumerator.Current
      {
        get { return Current; }
      }

      void IDisposable.Dispose()
      {
      }
    }

    private class EventHandlerInfo
    {
      private readonly EventInfo e;
      private readonly EventHandler h;

      public EventHandler EventHandler
      {
        get { return h; }
      }

      public EventInfo EventInfo
      {
        get { return e; }
      }

      public EventHandlerInfo(EventInfo eventInfo, EventHandler handler)
      {
        if (eventInfo == null)
          throw new ArgumentNullException("eventInfo");
        if (handler == null)
          throw new ArgumentNullException("handler");
        e = eventInfo;
        h = handler;
      }
    }

    private class SortEventArgs : EventArgs
    {
    }

    #endregion

    #region Fields

    private readonly IList<T> list;
    private bool isEditableObject;
    private readonly bool supportsListChanged;
    private bool supportsNotifyPropertyChanged;
    private bool supportsPropertyChangedEvents;
    private ListItemChangeEvents monitorItemChanges = ListItemChangeEvents.None;
    [NonSerialized] private PropertyDescriptorCollection itemProperties;
    [NonSerialized] private Dictionary<string, EventHandlerInfo> itemPropertyChangedEvents;
    private bool allowEdit = true;
    private bool allowNew = true;
    private bool allowRemove = true;
    private ObjectView<T> newItemPending;
    private SortDescriptionCollection sortProps;
    private List<int> sortIndexes = new List<int>();
    private int lastSortingPropertyIndex;
    private int version;
    private string filter;
    [NonSerialized] private Predicate<T> filterPredicate;
    private bool userFilterPredicate;
    [NonSerialized] private PropertyDescriptorCollection filterProperties;
    private readonly Queue<EventArgs> eventQueue = new Queue<EventArgs>();
    private bool suppressUpdates;
    [NonSerialized] private PropertyComparerCollection propertyComparers;
    [NonSerialized] private ListChangedEventHandler listChangedEvent;
    [NonSerialized] private EventHandler<AddingNewEventArgs> addingNewEvent;
    [NonSerialized] private EventHandler sortedEvent;
    [NonSerialized] private EventHandler<RemovingItemEventArgs> removingItemEvent;
    [NonSerialized] private ListChangedEventHandler beforeListChangedEvent;
    [NonSerialized] private ListChangedEventHandler afterListChangedEvent;
    /// <summary>
    /// The return non browseable properties in ITypedList method GetItemProperties
    /// </summary>
    public static bool IncludeNonBrowseable;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the filter predicate.
    /// </summary>
    /// <remarks>
    /// A delegate that performs filtering on the items in the list.  FilterPredicate is a code-based alternative
    /// to the <see cref="Filter"/> expression property.  If both Filter and FilterPredicate have been set, the last one set
    /// takes precedence.  Setting either Filter or FilterPredicate to null removes the filter from the view.
    /// </remarks>
    /// <returns>The filter predicate.</returns>
    public Predicate<T> FilterPredicate
    {
      get { return userFilterPredicate ? filterPredicate : null; }
      set
      {
        if (filterPredicate != value)
        {
          filterPredicate = value;
          userFilterPredicate = filterPredicate != null;
          filterProperties = null;
          ApplyFilter();
          RaiseEvents();
        }
      }
    }

    /// <summary>
    /// Gets the underlying list.
    /// </summary>
    /// <returns>The list supplied in the ObjectListView constructor.</returns>
    public IList<T> List
    {
      get { return list; }
    }

    /// <summary>
    /// Gets or sets a value indicating how list item changes should be monitored.
    /// </summary>
    /// <remarks>
    /// When this value is set to a value other than <b>None</b>, ObjectListView will raise <see cref="ListChanged"/> events when list item
    /// property values change, in addition to when the underlying list raises <see cref="System.ComponentModel.BindingList{T}.ListChanged"/>.<br></br><br></br>
    /// Normally the underlying list raises <see cref="System.ComponentModel.BindingList{T}.ListChanged"/> events when list item property values change, and ListItemChangeMonitoring may be set
    /// to <b>None</b>.  This property is set automatically for common scenarios, and is needed only in advanced scenarios when the underlying data list does
    /// not correctly support list item change notifications.  For example, <see cref="System.ComponentModel.BindingList{T}"/> raises ListChanged for item changes when the list item
    /// type implements <see cref="System.ComponentModel.INotifyPropertyChanged"/>, but not when <b><i>property</i>NameChanged</b> events are raised.
    /// </remarks>
    /// <returns>
    /// 	<b>None</b> if ListChanged should be raised only when the underlying list raises ListChanged.<br></br>
    ///		<b>INotifyPropertyChanged</b> if ListChanged should be raised when a list item raises <seealso cref="INotifyPropertyChanged.PropertyChanged"/>.<br></br>
    ///		<b>PropertyChangedEvents</b> if ListChanged should be raised when a list item raises a <b><i>propertyName</i>Changed</b> event.
    /// </returns>
    public ListItemChangeEvents ListItemChangeMonitoring
    {
      get { return monitorItemChanges; }
      set
      {
        if (value != monitorItemChanges)
        {
          var wasMonitoring = (monitorItemChanges != ListItemChangeEvents.None);
          monitorItemChanges = value;
          var isMonitoring = (monitorItemChanges != ListItemChangeEvents.None);
          if (list.Count > 0)
            foreach (var item in list)
            {
              if (wasMonitoring)
                UnwirePropertyChangedEvents(item);
              if (isMonitoring)
                WirePropertyChangedEvents(item);
            }
        }

        RaiseEvents();
      }
    }

    /// <summary>
    /// Gets the collection of <seealso cref="IComparer"/> objects used to compare property values when sorting.
    /// </summary>
    /// <remarks>
    /// If a property comparer is not provided, the default <seealso cref="IComparable"/> implementation of the property type is used.
    /// </remarks>
    /// <returns>The property comparers.  If no comparers have been added, an empty collection is returned.</returns>
    public PropertyComparerCollection PropertyComparers
    {
      get { return propertyComparers; }
    }

    /// <summary>
    /// Gets or sets the sort for the view.
    /// </summary>
    /// <remarks>
    /// This property is an alternative to the ApplySort methods.  It consists of a list of property names separaterd by commas.
    /// Each property name can optionally be followed by ASC or DESC, indicating an ascending or descending sort direction for that property.
    /// If a direction is not specified, the sort for that column will be ascending.
    /// </remarks>
    /// 
    /// <example>
    /// <code>
    ///		ObjectListView view = new ObjectListView(someList);
    ///		view.Sort = "LastName, State DESC";
    /// ...
    ///		view.Sort = "Date ASC, Name ASC, Orders DESC";
    /// ...
    ///		view.Sort = "AccountID ASC";
    /// </code>
    /// </example>
    /// <value>The sort.</value>
    public string Sort
    {
      get
      {
        var sb = new StringBuilder();

        if (sortProps != null)
          for (var i = 0; i < sortProps.Count; i++)
          {
            if (i == 0)
              sb.AppendFormat("{0} {1}", sortProps[i].PropertyName, sortProps[i].Direction == ListSortDirection.Ascending ? "ASC" : "DESC");
            else
              sb.AppendFormat(", {0} {1}", sortProps[i].PropertyName, sortProps[i].Direction == ListSortDirection.Ascending ? "ASC" : "DESC");
          }
        return sb.ToString();
      }
      set
      {
        if (string.IsNullOrEmpty(value))
          RemoveSort();
        else
        {
          var descs = new SortDescriptionCollection(typeof (T));

          var terms = value.Split(',');
          foreach (var term in terms)
          {
            var parts = term.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
              throw new ArgumentException("Sort");

            var propertyName = parts[0].TrimStart(new[] {' ', '['}).TrimEnd(new[] {' ', ']'});

            var direction = ListSortDirection.Ascending;
            if (parts.Length > 1)
            {
              var dir = parts[1].ToUpper();
              if (dir == "DESC")
                direction = ListSortDirection.Descending;
              else if (dir != "ASC")
                throw new ArgumentException("Sort");
            }

            // If the property name is of the form "A.B", it is assumed to refer to a property of a property (a property path).
            if (propertyName.Contains("."))
            {
              PropertyDescriptor property = new PropertyPathDescriptor(typeof (T), propertyName);
              descs.Add(new SortDescription(property, direction));
            }
            else
              descs.Add(new SortDescription(propertyName, direction));
          }

          ApplySort(descs.GetListSortDescriptions());
        }
      }
    }

    private bool IsFiltered
    {
      get { return (filterPredicate != null); }
    }

    private bool SortIndexesDirty
    {
      get
      {
        // The capacity of the indexes list is the size of the list as the view knows it.
        // If the size of the list has changed (items added or removed), we know that the indexes are now invalid.
        // This can happen if the list is manipulated directly (not through methods of the view) and the list does not
        // notify the view of the changes.
        return (sortIndexes.Capacity != list.Count);
      }
    }

    private ListSortDescriptionCollection SortProperties
    {
      get { return sortProps.GetListSortDescriptions(); }
      set
      {
        var proposed = new SortDescriptionCollection(typeof (T), value);

        if (IsSortDifferent(proposed))
        {
          sortProps = proposed;

          // This prevents an uncommitted new row (from AddNew()) from being relocated in the sort.
          if (newItemPending != null)
            CancelAddNew();

          ApplySortCore();
        }
      }
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when a list item is added to the view with <see cref="AddNew"/>.
    /// </summary>
    public event EventHandler<AddingNewEventArgs> AddingNew
    {
      add { addingNewEvent = (EventHandler<AddingNewEventArgs>) Delegate.Combine(addingNewEvent, value); }
      remove { addingNewEvent = (EventHandler<AddingNewEventArgs>) Delegate.Remove(addingNewEvent, value); }
    }

    /// <summary>
    /// Raised after <seealso cref="ListChanged"/>.
    /// </summary>
    public event ListChangedEventHandler AfterListChanged
    {
      add { afterListChangedEvent = (ListChangedEventHandler) Delegate.Combine(afterListChangedEvent, value); }
      remove { afterListChangedEvent = (ListChangedEventHandler) Delegate.Remove(afterListChangedEvent, value); }
    }

    /// <summary>
    /// Raised before <seealso cref="ListChanged"/>.
    /// </summary>
    public event ListChangedEventHandler BeforeListChanged
    {
      add { beforeListChangedEvent = (ListChangedEventHandler) Delegate.Combine(beforeListChangedEvent, value); }
      remove { beforeListChangedEvent = (ListChangedEventHandler) Delegate.Remove(beforeListChangedEvent, value); }
    }

    /// <summary>
    /// Raised when a list item is removed with <see cref="Remove"/> or <see cref="RemoveAt"/>
    /// </summary>
    public event EventHandler<RemovingItemEventArgs> RemovingItem
    {
      add { removingItemEvent = (EventHandler<RemovingItemEventArgs>) Delegate.Combine(removingItemEvent, value); }
      remove { removingItemEvent = (EventHandler<RemovingItemEventArgs>) Delegate.Remove(removingItemEvent, value); }
    }

    /// <summary>
    /// Raised when the view is sorted.
    /// </summary>
    /// <remarks>
    /// The view can be sorted explicitly, with a call to <see cref="ApplySort(System.ComponentModel.ListSortDescriptionCollection)"/>,
    /// or implicitly when a sort has been applied and a list item is added or changed.
    /// </remarks>
    public event EventHandler Sorted
    {
      add { sortedEvent = (EventHandler) Delegate.Combine(sortedEvent, value); }
      remove { sortedEvent = (EventHandler) Delegate.Remove(sortedEvent, value); }
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectListView&lt;T&gt;"/> class.
    /// </summary>
    /// <param name="list">The list of objects to view.</param>
    public ObjectListView(IList<T> list)
    {
      if (list == null)
        throw new ArgumentNullException("list");

      this.list = list;

      if (ListSupportsListChanged(list))
      {
        supportsListChanged = true;
        WireListChangedEvent(list);
      }

      // Monitor list item change events.
      monitorItemChanges = ListItemChangeEvents.INotifyPropertyChanged | ListItemChangeEvents.PropertyChangedEvents;

      // If the list implements IRaiseItemChangedEvents (e.g. BindingList<T>), it will convert list item changes for INotifyChanged.PropertyChanged
      // into ListChanged events, but not for .NET 1.x-style propertyNameChanged events.
      if (list is IRaiseItemChangedEvents)
        if (((IRaiseItemChangedEvents) list).RaisesItemChangedEvents)
          monitorItemChanges = ListItemChangeEvents.PropertyChangedEvents;

      // Gather information about the list item type.
      SetListItemType();

      // If needed, attach handlers to item change events for existing list items.
      if (list.Count > 0)
        if (monitorItemChanges != ListItemChangeEvents.None)
          foreach (var item in list)
            WirePropertyChangedEvents(item);

      RebuildSortIndexes();

      allowEdit = !list.IsReadOnly;
      allowNew = !list.IsReadOnly;
      allowRemove = !list.IsReadOnly;
    }

    /// <summary>
    /// Applies the existing <see cref="Filter"/> or <see cref="FilterPredicate"/>.
    /// </summary>
    /// <remarks>
    /// This method is only required when using a <see cref="FilterPredicate"/> that contains criteria that varies over time, or that references
    /// criteria other than that based on properties of the the list items (for example, criteria based on a property of a class that is a
    /// list item property).
    /// </remarks>
    public void ApplyFilter()
    {
      if (!suppressUpdates)
      {
        // Invalidate enumerators.
        version++;

        RebuildSortIndexes();
      }

      QueueEvent(new ListChangedEventArgs(ListChangedType.Reset, -1));
    }

    /// <summary>
    /// Suppresses sorting, filtering, and <see cref="ListChanged"/> events until <see cref="EndUpdate"/> is called.
    /// </summary>
    /// <remarks>
    /// This improves performance when making a large number of changes to the underlying list.  When <see cref="EndUpdate"/> is called,
    /// the pending <see cref="ListChanged"/> events are replaced with a single ListChanged+Reset event.  Sorting and filtering is also
    /// deferred until EndUpdate is called.
    /// </remarks>
    public void BeginUpdate()
    {
      suppressUpdates = true;

      if (propertyComparers != null)
        propertyComparers.BeginUpdate();
    }

    /// <summary>
    /// Resumes the raising of <see cref="ListChanged"/> events that have been suppressed with a previous call to <see cref="BeginUpdate()"/>.
    /// </summary>
    /// <remarks>
    /// If there are any pending <see cref="ListChanged"/> events since <see cref="BeginUpdate()"/> was called, these are cleared, and
    /// a single ListChanged+Reset event is raised.
    /// </remarks>
    public void EndUpdate()
    {
      if (propertyComparers != null)
        propertyComparers.EndUpdate();

      suppressUpdates = false;

      // If ListChanged events have been queued, sort and filter, and raise a single ListChanged+Reset event.
      if (eventQueue.Count > 0)
      {
        OnReset(); // this may generate both Sorted and ListChanged+Reset queued events.

        eventQueue.Clear();
        QueueEvent(new ListChangedEventArgs(ListChangedType.Reset, -1));
        RaiseEvents();
      }
    }

    /// <summary>
    /// Finds the index of the first list item matching the specified criteria.
    /// </summary>
    /// <remarks>
    /// The find criteria can be any expression that would be valid for the <seealso cref="Filter"/> property.
    /// </remarks>
    /// <param name="criteria">The criteria.</param>
    /// <returns>The index of the list item found, or -1 if not found.</returns>
    public int Find(string criteria)
    {
      if (criteria == null)
        throw new ArgumentNullException("criteria");
      if (criteria == "")
        throw new ArgumentException("criteria");

      var expressionTree = FilterNode.Parse(criteria);
      var evaluator = new FilterEvaluator<T>(expressionTree, itemProperties);

      for (var i = 0; i < Count; i++)
      {
        if (evaluator.Matches(this[i]))
          return i;
      }

      return -1;
    }

    /// <summary>
    /// Finds the index of the first item matching the specified criteria.
    /// </summary>
    /// <remarks>
    /// This overload of Find is a code-based alternative to providing a string expression criteria.
    /// Find returns the first item for which predicate returns true.
    /// </remarks>
    /// <param name="predicate">Delegate that evaluates list items.</param>
    /// <returns>The index of the list item found, or -1 if not found.</returns>
    public int Find(Predicate<T> predicate)
    {
      if (predicate == null)
        throw new ArgumentNullException("predicate");

      for (var i = 0; i < Count; i++)
      {
        if (predicate(this[i]))
          return i;
      }

      return -1;
    }

    /// <summary>
    /// Returns the list items that match the specified criteria.
    /// </summary>
    /// <remarks>
    /// The select criteria can be any expression that would be valid for the <seealso cref="Filter"/> property.
    /// </remarks>
    /// <param name="criteria">The criteria.</param>
    /// <returns>The matching items.  If no items match the critiera, an empty list is returned.</returns>
    public IList<T> Select(string criteria)
    {
      if (criteria == null)
        throw new ArgumentNullException("criteria");
      if (criteria == "")
        throw new ArgumentException("criteria");

      var expressionTree = FilterNode.Parse(criteria);
      var evaluator = new FilterEvaluator<T>(expressionTree, itemProperties);

      var matches = new List<T>();

      foreach (var item in this)
      {
        if (evaluator.Matches(item))
          matches.Add(item);
      }

      return matches;
    }

    /// <summary>
    /// Returns the list items that match the specified criteria.
    /// </summary>
    /// <remarks>
    /// This overload of Select is a code-based alternative to providing a string expression criteria.
    /// Select returns all of the items for which predicate returns true.
    /// </remarks>
    /// <param name="predicate">Delegate that evaluates list items.</param>
    /// <returns>The matching items.  If no items match the critiera, an empty list is returned.</returns>
    public IList<T> Select(Predicate<T> predicate)
    {
      if (predicate == null)
        throw new ArgumentNullException("predicate");

      var matches = new List<T>();

      foreach (var item in this)
      {
        if (predicate(item))
          matches.Add(item);
      }

      return matches;
    }

    /// <summary>
    /// Copies the list items in the view to a new array of the list item type.
    /// </summary>
    /// <returns>
    /// An array of the list item type containing all of the items in the view.  If there are no items in the view, a zero-length
    /// array is returned.
    /// </returns>
    public T[] ToArray()
    {
      var arr = new T[Count];
      CopyTo(arr, 0);
      return arr;
    }

    #region ICancelAddNew Members

    void ICancelAddNew.CancelNew(int itemIndex)
    {
      if (IsPendingNewItem(itemIndex))
        CancelAddNew();

      RaiseEvents();
    }

    void ICancelAddNew.EndNew(int itemIndex)
    {
      if (IsPendingNewItem(itemIndex))
        FinishAddNew();

      RaiseEvents();
    }

    #endregion

    #region IRaiseItemChangedEvents Members

    bool IRaiseItemChangedEvents.RaisesItemChangedEvents
    {
      get { return true; }
    }

    #endregion

    #region ITypedList Members

    PropertyDescriptorCollection ITypedList.GetItemProperties(PropertyDescriptor[] listAccessors)
    {
      return itemProperties;
    }

    string ITypedList.GetListName(PropertyDescriptor[] listAccessors)
    {
      return "";
    }

    #endregion

    #region IBindingListView Members

    /// <summary>
    /// Sorts the view of the underlying list based on the given <see cref="T:System.ComponentModel.ListSortDescriptionCollection"></see>.
    /// </summary>
    /// <param name="sorts">The <see cref="T:System.ComponentModel.ListSortDescriptionCollection"></see> containing the sorts to apply to the view.</param>
    public void ApplySort(ListSortDescriptionCollection sorts)
    {
      ListSortDescriptionCollection proposed;

      if (sorts == null)
        proposed = new ListSortDescriptionCollection();
      else
      {
        foreach (ListSortDescription desc in sorts)
          ValidateProperty(desc.PropertyDescriptor);

        var props = new ListSortDescription[sorts.Count];
        sorts.CopyTo(props, 0);
        proposed = new ListSortDescriptionCollection(props);
      }

      SortProperties = proposed;

      RaiseEvents();
    }

    /// <summary>
    /// Gets or sets the filter to be used to exclude items from the view of the underlying list.
    /// </summary>
    /// <remarks>
    /// The filter expression consists of one or more relational expressions comparing a list item property with a literal value.
    /// The supported relational operators are &lt; &lt;= = == &gt;= &gt; &lt;&gt; !=.  Relational expressions can be connected with AND or OR.
    /// String literals must be quoted if they include whitespace or operators.
    /// Properties of properties ("property paths") may be expressed with a dotted notation: <italic>property1.property2.property3</italic>.
    /// 
    /// <example>
    /// <code>
    ///		ObjectListView view = new ObjectListView(someList);
    ///		view.Filter = "LastName = Smith AND State = WA";
    /// ...
    ///		view.Filter = "Orders > 5 OR Date = 12/31/2005";
    /// ...
    ///		view.Filter = "Customer.AccountRep.Department == 12";
    /// </code>
    /// </example>
    /// 
    /// <bold>Note:</bold> Automatic update of the view when a property of a filter property path changes is not supported in this release.
    /// 
    /// Filter and <see cref="FilterPredicate"/> are mutually exclusive.  If both Filter and FilterPredicate have been set,
    /// the last one set takes precedence.  Setting either Filter or FilterPredicate to null removes the filter from the view.
    /// </remarks>
    /// <returns>The string used to filter items out in the view of the underlying list, or <b>null</b> if no filter is set.</returns>
    public string Filter
    {
      get { return filter; }
      set
      {
        if (!string.IsNullOrEmpty(value))
        {
          var expressionTree = FilterNode.Parse(value);
          var evaluator = new FilterEvaluator<T>(expressionTree, itemProperties);

          filterPredicate = evaluator.Matches;
          filterProperties = evaluator.FilterProperties;
        }
        else
        {
          filterPredicate = null;
          filterProperties = null;
        }

        filter = value;
        userFilterPredicate = false;

        ApplyFilter();

        RaiseEvents();
      }
    }

    /// <summary>
    /// Removes the current filter applied to the view.
    /// </summary>
    public void RemoveFilter()
    {
      Filter = null;
    }

    /// <summary>
    /// Gets the collection of sort descriptions currently applied to the view.
    /// </summary>
    /// <returns>
    /// The <see cref="T:System.ComponentModel.ListSortDescriptionCollection"></see> currently applied to the view.  If no sort
    /// is currently in effect, an empty collection is returned.
    /// </returns>
    public ListSortDescriptionCollection SortDescriptions
    {
      get { return sortProps.GetListSortDescriptions(); }
    }

    bool IBindingListView.SupportsAdvancedSorting
    {
      get { return true; }
    }

    bool IBindingListView.SupportsFiltering
    {
      get { return true; }
    }

    #endregion

    #region IBindingList Members

    void IBindingList.AddIndex(PropertyDescriptor property)
    {
      ValidateProperty(property);

      // No-op.  DataView implementation adds to find indexes used later by Find(), but if not present when Find() is called, the index
      // is looked up at that time.
    }

    /// <summary>
    /// Adds a new item to the list.
    /// </summary>
    /// <returns>The item added to the list (wrapped in an <see cref="ObjectView{T}"/>).</returns>
    /// <exception cref="T:System.Data.DataException"><see cref="P:System.ComponentModel.IBindingList.AllowNew"></see> is false. </exception>
    public object AddNew()
    {
      ObjectView<T> wrapper = null;

      if (!allowNew)
        throw new DataException("AllowNew is set to false.");

      T newItem;

      var args = new AddingNewEventArgs();
      OnAddingNew(args);

      if (args.UserSetNewObject)
        newItem = args.NewObject;
      else
        newItem = (T) Activator.CreateInstance(typeof (T));

      wrapper = new ObjectView<T>(newItem);

      // If an item was previously added with AddNew() but has not yet been committed with item.EndEdit(), commit it now.
      if (newItemPending != null)
        FinishAddNew();

      newItemPending = wrapper;

      // If the item type is IEditable object, a newly added item will be removed from the list if IEditableObject.CancelEdit is called before
      // the next call to AddNew().  When IEditableObject.EndEdit() is called, ListChanged+ItemAdded is raised again for the same item.
      if (isEditableObject)
      {
        ((IEditableObjectEvents) newItemPending).Ended += editableListItem_Ended;
        ((IEditableObjectEvents) newItemPending).Cancelled += editableListItem_Cancelled;
      }

      Add(newItem);

      RaiseEvents();

      return wrapper;
    }

    /// <summary>
    /// Gets whether you can update items in the list.
    /// </summary>
    /// <returns><b>true</b> if you can update the items in the list; otherwise, <b>false</b>.</returns>
    public bool AllowEdit
    {
      get { return allowEdit; }
      set
      {
        if (value && list.IsReadOnly)
          throw new InvalidOperationException("The list is read only.");
        allowEdit = value;
      }
    }

    /// <summary>
    /// Gets whether you can add items to the list using <see cref="AddNew"/>
    /// </summary>
    /// <returns><b>true</b> if you can add items to the list using <see cref="AddNew"></see>; otherwise, <b>false</b>.</returns>
    public bool AllowNew
    {
      get { return allowNew; }
      set
      {
        if (value && list.IsReadOnly)
          throw new InvalidOperationException("The list is read only.");
        allowNew = value;
      }
    }

    /// <summary>
    /// Gets whether you can remove items from the list, using <see cref="M:System.Collections.IList.Remove(System.Object)"></see> or <see cref="M:System.Collections.IList.RemoveAt(System.Int32)"></see>.
    /// </summary>
    /// <returns><b>true</b> if you can remove items from the list; otherwise, <b>false</b>.</returns>
    public bool AllowRemove
    {
      get { return allowRemove; }
      set
      {
        if (value && list.IsReadOnly)
          throw new InvalidOperationException("The list is read only.");
        allowRemove = value;
      }
    }

    /// <summary>
    /// Sorts the view based on a <see cref="T:System.ComponentModel.PropertyDescriptor"></see> and a <see cref="T:System.ComponentModel.ListSortDirection"></see>.
    /// </summary>
    /// <param name="property">The <see cref="T:System.ComponentModel.PropertyDescriptor"></see> to sort by.</param>
    /// <param name="direction">One of the <see cref="T:System.ComponentModel.ListSortDirection"></see> values.</param>
    public void ApplySort(PropertyDescriptor property, ListSortDirection direction)
    {
      ListSortDescriptionCollection proposed;

      if (property == null)
        proposed = new ListSortDescriptionCollection();
      else
      {
        ValidateProperty(property);

        var props = new[] {new ListSortDescription(property, direction)};
        proposed = new ListSortDescriptionCollection(props);
      }

      SortProperties = proposed;

      RaiseEvents();
    }

    /// <summary>
    /// Returns the index of the list item that has the given property value.
    /// </summary>
    /// <param name="property">The <see cref="T:System.ComponentModel.PropertyDescriptor"></see> representing the property to search on.</param>
    /// <param name="key">The value of the property parameter to search for.</param>
    /// <returns>
    /// The index of the list item that has the given property value, or <b>-1</b> if a matching item was not found.
    /// </returns>
    int IBindingList.Find(PropertyDescriptor property, object key)
    {
      ValidateProperty(property);

      for (var i = 0; i < list.Count; i++)
      {
        var listValue = property.GetValue(list[i]);
        if (Equals(listValue, key))
          return GetSortedPositionOfListIndex(i);
      }

      return -1;
    }

    /// <summary>
    /// Gets whether the items in the view are sorted.
    /// </summary>
    /// <returns><b>true</b> if <see cref="M:System.ComponentModel.IBindingList.ApplySort(System.ComponentModel.PropertyDescriptor,System.ComponentModel.ListSortDirection)"></see> has been called and <see cref="M:System.ComponentModel.IBindingList.RemoveSort"></see> has not been called; otherwise, <b>false</b>.</returns>
    public bool IsSorted
    {
      get { return (sortProps.Count > 0); }
    }

    /// <summary>
    /// Raised when a list item is added to the list, changed, or removed from the list.
    /// </summary>
    public event ListChangedEventHandler ListChanged
    {
      add { listChangedEvent = (ListChangedEventHandler) Delegate.Combine(listChangedEvent, value); }
      remove { listChangedEvent = (ListChangedEventHandler) Delegate.Remove(listChangedEvent, value); }
    }

    void IBindingList.RemoveIndex(PropertyDescriptor property)
    {
      ValidateProperty(property);

      // No-op.  DataView implementation adds to find indexes used later by Find(), but if not present when Find() is called, the index
      // is looked up at that time.
    }

    /// <summary>
    /// Removes any sort applied using <see cref="M:System.ComponentModel.IBindingList.ApplySort(System.ComponentModel.PropertyDescriptor,System.ComponentModel.ListSortDirection)"></see>.
    /// </summary>
    public void RemoveSort()
    {
      SortProperties = new ListSortDescriptionCollection();
      RaiseEvents();
    }

    /// <summary>
    /// Gets the direction of the current sort.
    /// </summary>
    /// <remarks>
    /// If the current sort includes more than one property, the direction of the primary sort property is returned.
    /// If no sort is in effect, <b>Ascending</b> is returned.
    /// </remarks>
    /// <returns>One of the <see cref="T:System.ComponentModel.ListSortDirection"></see> values.</returns>
    public ListSortDirection SortDirection
    {
      get
      {
        ListSortDirection dir;

        if (sortProps.Count > 0)
          dir = sortProps[0].Direction;
        else
          dir = ListSortDirection.Ascending;

        return dir;
      }
    }

    /// <summary>
    /// Gets the <see cref="T:System.ComponentModel.PropertyDescriptor"></see> that is being used for sorting.
    /// </summary>
    /// <remarks>
    /// If the current sort includes more than one property, the primary sort property is returned.
    /// If no sort is in effect, <b>null</b> is returned.
    /// </remarks>
    /// <returns>
    /// The <see cref="T:System.ComponentModel.PropertyDescriptor"></see> that is being used for sorting.
    /// </returns>
    public PropertyDescriptor SortProperty
    {
      get
      {
        PropertyDescriptor prop;

        if (sortProps.Count > 0)
          prop = itemProperties.Find(sortProps[0].PropertyName, true);
        else
          prop = null;

        return prop;
      }
    }

    bool IBindingList.SupportsChangeNotification
    {
      get
      {
        // My first inclination was to report whether the underlying list supports the ListChanged event.  However,
        // if we don't return true, a bound control won't subscribe to our ListChanged event,
        // and thus won't receive ANY changed events even if they are raised through ObjectListView instead of the underlying list.
        // By returning true, we provide correct binding for sorting and filtering changes, and for list manipulation through ObjectListView
        // (but not through the methods of the underlying list, unless the list also raises ListChanged).
        return true;
      }
    }

    bool IBindingList.SupportsSearching
    {
      get { return true; }
    }

    bool IBindingList.SupportsSorting
    {
      get { return itemProperties.Count > 0; }
    }

    #endregion

    #region IList<T> Members

    /// <summary>
    /// Determines the index of a specific item in the view.
    /// </summary>
    /// <param name="item">The object to locate in the view.</param>
    /// <returns>
    /// The index of <i>item</i> if found in the view; otherwise, <b>-1</b>.
    /// </returns>
    public int IndexOf(T item)
    {
      var index = list.IndexOf(item);
      return GetSortedPositionOfListIndex(index);
    }

    /// <summary>
    /// Inserts an item into the list at the specified view index.
    /// </summary>
    /// <remarks>
    /// If the view is sorted, inserting at a specific position is not meaningul; the new item will appear at the index
    /// appropriate to the it's position in the sort.
    /// If the view is filtered, the index specified is interpreted is the view index of the new item.  The position in the
    /// underlying list may be different.
    /// </remarks>
    /// <param name="index">The zero-based view index at which item should be inserted.</param>
    /// <param name="item">The object to insert into the list.</param>
    /// <exception cref="T:System.Data.DataException"><see cref="AllowNew"/> is false.</exception>
    /// <exception cref="T:System.ArgumentOutOfRangeException">index is not a valid index in the view.</exception>
    public void Insert(int index, T item)
    {
      if (!allowNew)
        throw new DataException("AllowNew is set to false.");

      // If the view is sorted, inserting at a specific point is not meaningful - the item will appear at the index appropriate to
      // it's position in the sort.  Just add to the end of the list and re-sort.
      int listIndex;
      if (IsSorted)
      {
        list.Add(item);
        listIndex = list.IndexOf(item);
      }

        // If not sorted, insert the item into the requested list position.
      else
      {
        // If filtered, the index specified is the view index of the filtered items.
        // For example, a list of { A B C D E F }, filtered to { B D F },
        // Insert(1, G) results in filtered { B G D F } (if G passes the filter), { A B C G D E F } in the underlying list.
        if (IsFiltered)
          index = GetListPositionOfViewIndex(index);
        list.Insert(index, item);
        listIndex = index;
      }

      if (!supportsListChanged)
        OnItemAdded(listIndex);

      RaiseEvents();
    }

    /// <summary>
    /// Removes the <see cref="T:System.Collections.IList"></see> item at the specified view index.
    /// </summary>
    /// <param name="index">The zero-based view index of the item to remove.</param>
    /// <exception cref="T:System.ArgumentOutOfRangeException">index is not a valid index in the view. </exception>
    /// <exception cref="T:System.Data.DataException"><see cref="AllowRemove"/> is false.</exception>
    public void RemoveAt(int index)
    {
      RemoveCore(index);
      RaiseEvents();
    }

    /// <summary>
    /// Gets or sets the list item at the specified index in the view.
    /// </summary>
    public T this[int index]
    {
      get { return list[GetListPositionOfViewIndex(index)]; }
      set
      {
        if (!allowEdit)
          throw new DataException("AllowEdit is set to false.");

        var listIndex = GetListPositionOfViewIndex(index);

        var existingItem = list[listIndex];
        if (existingItem != null)
          UnwirePropertyChangedEvents(existingItem);

        list[listIndex] = value;

        if (value != null)
          WirePropertyChangedEvents(value);

        if (!supportsListChanged)
          OnItemChanged(listIndex, (PropertyDescriptor) null);

        RaiseEvents();
      }
    }

    #endregion

    #region IList Members

    int IList.Add(object value)
    {
      var sortedIndex = -1;

      if (!allowNew)
        throw new DataException("AllowNew is set to false.");

      list.Add((T) value);
      var index = list.IndexOf((T) value);

      // If the list supports ListChanged, OnItemAdded will have already been called.
      if (!supportsListChanged)
        sortedIndex = OnItemAdded(index);
      else
        // If the add is the result of AddNew(), the new item isn't in the sort.  The item will be put into the sort when the add
        // is committed with EndNew() or IEditableObject.EndEdit().
        if (IsPendingNewItem(index))
          sortedIndex = index;
        else
          sortedIndex = GetSortedPositionOfListIndex(index);

      RaiseEvents();

      return sortedIndex;
    }

    void IList.Clear()
    {
      Clear();
    }

    bool IList.Contains(object value)
    {
      return Contains((T) value);
    }

    int IList.IndexOf(object value)
    {
      return value is T ? IndexOf((T)value) : -1;
    }

    void IList.Insert(int index, object value)
    {
      Insert(index, (T) value);
    }

    bool IList.IsFixedSize
    {
      get { return false; }
    }

    bool IList.IsReadOnly
    {
      get { return false; }
    }

    void IList.Remove(object value)
    {
      Remove((T) value);
    }

    void IList.RemoveAt(int index)
    {
      RemoveAt(index);
    }

    object IList.this[int index]
    {
      get { return this[index]; }
      set { this[index] = (T) value; }
    }

    #endregion

    #region ICollection<T> Members

    /// <summary>
    /// Adds an item to the view.
    /// </summary>
    /// <param name="item">The object to add to the view.</param>
    /// <exception cref="T:System.Data.DataException"><see cref="AllowNew"/> is false.</exception>
    public void Add(T item)
    {
      if (!allowNew)
        throw new DataException("AllowNew is set to false.");

      list.Add(item);

      // If the list supports ListChanged, OnItemAdded will have already been called.
      if (!supportsListChanged)
        OnItemAdded(list.IndexOf(item));

      RaiseEvents();
    }

    /// <summary>
    /// Removes all items from the list.
    /// </summary>
    /// <exception cref="T:System.NotSupportedException">The list is read-only.</exception>
    public void Clear()
    {
      list.Clear();

      // If the list supports ListChanged, OnReset will have already been called.
      if (!supportsListChanged)
        OnReset();

      RaiseEvents();
    }

    /// <summary>
    /// Determines whether the view contains a specific value.
    /// </summary>
    /// <param name="item">The object to locate in the view.</param>
    /// <returns>
    /// <b>true</b> if item is found in the view; otherwise, <b>false</b>.
    /// </returns>
    public bool Contains(T item)
    {
      return list.Contains(item);
    }

    /// <summary>
    /// Copies the elements of the view to an <see cref="T:System.Array"></see>, starting at a particular <see cref="T:System.Array"></see> index.
    /// </summary>
    /// <param name="array">The one-dimensional <see cref="T:System.Array"></see> that is the destination of the elements copied from the view. The <see cref="T:System.Array"></see> must have zero-based indexing.</param>
    /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
    /// <exception cref="T:System.ArgumentOutOfRangeException">arrayIndex is less than 0.</exception>
    /// <exception cref="T:System.ArgumentNullException">array is null.</exception>
    /// <exception cref="T:System.ArgumentException">array is multidimensional.<br></br>-or-<br></br>arrayIndex is equal to or greater than the length of array.<br></br>-or-<br></br>The number of elements in the source <see cref="T:System.Collections.Generic.ICollection`1"></see> is greater than the available space from arrayIndex to the end of the destination array.<br></br>-or-<br></br>Type T cannot be cast automatically to the type of the destination array.</exception>
    public void CopyTo(T[] array, int arrayIndex)
    {
      for (var i = 0; i < Count; i++)
        array.SetValue(this[i], i + arrayIndex);
    }

    /// <summary>
    /// Gets the number of elements contained in the view.
    /// </summary>
    /// <returns>The number of elements contained in the view.</returns>
    public int Count
    {
      get { return GetSortIndexes().Count; }
    }

    /// <summary>
    /// Gets a value indicating whether the list is read-only.
    /// </summary>
    /// <returns><b>true</b> if the list is read-only; otherwise, <b>false</b>.</returns>
    public bool IsReadOnly
    {
      get { return list.IsReadOnly; }
    }

    /// <summary>
    /// Removes the first occurrence of a specific object from the list.
    /// </summary>
    /// <param name="item">The object to remove from the list.</param>
    /// <returns>
    /// <b>true</b> if item was successfully removed from the list; otherwise, <b>false</b>. This method also returns false if <i>item</i> is not found in the original list.
    /// </returns>
    /// <exception cref="T:System.Data.DataException"><see cref="AllowRemove"/> is false.</exception>
    public bool Remove(T item)
    {
      var index = IndexOf(item);
      if (index > -1)
      {
        RemoveCore(index);
        RaiseEvents();
        return true;
      }
      else
        return false;
    }

    #endregion

    #region ICollection Members

    void ICollection.CopyTo(Array array, int index)
    {
      CopyTo((T[]) array, index);
    }

    int ICollection.Count
    {
      get { return Count; }
    }

    bool ICollection.IsSynchronized
    {
      get { return false; }
    }

    object ICollection.SyncRoot
    {
      get { return null; }
    }

    #endregion

    #region IEnumerable<T> Members

    /// <summary>
    /// Returns an enumerator that iterates through the view.
    /// </summary>
    /// <returns>
    /// An <see cref="T:System.Collections.Generic.IEnumerator`1"></see> that can be used to iterate through the view.
    /// </returns>
    public IEnumerator<T> GetEnumerator()
    {
      return new ObjectListViewEnumerator(this);
    }

    #endregion

    #region IEnumerable Members

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    #endregion

    #region IDeserializationCallback Members

    /// <summary>
    /// Runs when the entire object graph has been deserialized.
    /// </summary>
    /// <remarks>
    /// This method is provided only as a serialization helper for the debugger visualizer.
    /// </remarks>
    /// <param name="sender">The object that initiated the callback.</param>
    void IDeserializationCallback.OnDeserialization(object sender)
    {
      if (userFilterPredicate)
        filterPredicate = new Predicate<T>(FilterPredicatePlaceholder);
      else
        filterPredicate = null;

      SetItemProperties(typeof(T));
    }

    private bool FilterPredicatePlaceholder(T listItem)
    {
      return true;
    }

    #endregion

    /// <summary>
    /// Raises the <see cref="AddingNew"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    /// <param name="args">The <see cref="AddingNewEventArgs"/> instance containing the event data.</param>
    protected virtual void OnAddingNew(AddingNewEventArgs args)
    {
      if (addingNewEvent != null)
        addingNewEvent(this, args);
    }

    /// <summary>
    /// Raises the <see cref="AfterListChanged"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    /// <param name="args">The <see cref="System.ComponentModel.ListChangedEventArgs"/> instance containing the event data.</param>
    protected virtual void OnAfterListChanged(ListChangedEventArgs args)
    {
      if (afterListChangedEvent != null)
        afterListChangedEvent(this, args);
    }

    /// <summary>
    /// Raises the <see cref="BeforeListChanged"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    /// <param name="args">The <see cref="System.ComponentModel.ListChangedEventArgs"/> instance containing the event data.</param>
    protected virtual void OnBeforeListChanged(ListChangedEventArgs args)
    {
      if (beforeListChangedEvent != null)
        beforeListChangedEvent(this, args);
    }

    /// <summary>
    /// Raises the <see cref="ListChanged"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    /// <param name="args">The <see cref="System.ComponentModel.ListChangedEventArgs"/> instance containing the event data.</param>
    protected virtual void OnListChanged(ListChangedEventArgs args)
    {
      if (listChangedEvent != null)
        try
        {
          listChangedEvent(this, args);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Raises the <see cref="RemovingItem"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    /// <param name="args">The <see cref="RemovingItemEventArgs"/> instance containing the event data.</param>
    protected virtual void OnRemovingItem(RemovingItemEventArgs args)
    {
      if (removingItemEvent != null)
        removingItemEvent(this, args);
    }

    /// <summary>
    /// Raises the <see cref="Sorted"/> event.
    /// </summary>
    /// <remarks>
    /// When overriding this method, you should call the base implementation to assure that the event is raised.
    /// </remarks>
    protected virtual void OnSorted()
    {
      if (sortedEvent != null)
        sortedEvent(this, EventArgs.Empty);
    }

    private static bool ListSupportsListChanged(IList<T> list)
    {
      if (list == null)
        throw new ArgumentNullException("list");

      if (list is IBindingList)
        return true;
      else
      {
        var listChanged = list.GetType().GetEvent("ListChanged");
        return (listChanged != null && listChanged.EventHandlerType == typeof (ListChangedEventHandler));
      }
    }

    private void WireListChangedEvent(IList<T> list)
    {
      if (list == null)
        throw new ArgumentNullException("list");

      if (list is IBindingList)
        ((IBindingList) list).ListChanged += list_ListChanged;
      else
      {
        var listChanged = list.GetType().GetEvent("ListChanged");
        if (listChanged != null)
          listChanged.AddEventHandler(list, new ListChangedEventHandler(list_ListChanged));
      }
    }

    private void SetListItemType()
    {
      var listItemType = typeof (T);

      isEditableObject = typeof (IEditableObject).IsAssignableFrom(listItemType);
      supportsNotifyPropertyChanged = typeof (INotifyPropertyChanged).IsAssignableFrom(listItemType);

      SetItemProperties(listItemType);
      itemPropertyChangedEvents = GetPropertyChangedEvents(listItemType);
      supportsPropertyChangedEvents = itemPropertyChangedEvents.Count > 0;

      sortProps = new SortDescriptionCollection(listItemType);

      propertyComparers = new PropertyComparerCollection(listItemType);
      propertyComparers.CollectionChanged += propertyComparers_CollectionChanged;
    }

    private void SetItemProperties(Type value)
    {
      itemProperties = IncludeNonBrowseable ? TypeDescriptor.GetProperties(value) : ListBindingHelper.GetListItemProperties(value);
    }

    private Dictionary<string, EventHandlerInfo> GetPropertyChangedEvents(Type itemType)
    {
      if (itemType == null)
        throw new ArgumentNullException("itemType");

      var events = new Dictionary<string, EventHandlerInfo>();

      foreach (var prop in itemType.GetProperties())
      {
        var propChangedEvent = itemType.GetEvent(prop.Name + "Changed");
        if (propChangedEvent != null && propChangedEvent.EventHandlerType == typeof (EventHandler))
        {
          EventHandler handler = delegate(object sender, EventArgs args)
                                   {
                                     OnItemChanged(list.IndexOf((T) sender), prop.Name);
                                     RaiseEvents();
                                   };

          events.Add(prop.Name, new EventHandlerInfo(propChangedEvent, handler));
        }
      }

      return events;
    }

    private void WirePropertyChangedEvents(T item)
    {
      if ((monitorItemChanges & ListItemChangeEvents.INotifyPropertyChanged) != 0 && supportsNotifyPropertyChanged)
        ((INotifyPropertyChanged) item).PropertyChanged += listItem_PropertyChanged;
      else if ((monitorItemChanges & ListItemChangeEvents.PropertyChangedEvents) != 0 && supportsPropertyChangedEvents)
        foreach (var pair in itemPropertyChangedEvents)
        {
          pair.Value.EventInfo.AddEventHandler(item, pair.Value.EventHandler);
        }
    }

    private void UnwirePropertyChangedEvents(T item)
    {
      if ((monitorItemChanges & ListItemChangeEvents.INotifyPropertyChanged) != 0 && supportsNotifyPropertyChanged)
        ((INotifyPropertyChanged) item).PropertyChanged -= listItem_PropertyChanged;
      else if ((monitorItemChanges & ListItemChangeEvents.PropertyChangedEvents) != 0 && supportsPropertyChangedEvents)
        foreach (var pair in itemPropertyChangedEvents)
        {
          pair.Value.EventInfo.RemoveEventHandler(item, pair.Value.EventHandler);
        }
    }

    private void ValidateProperty(PropertyDescriptor property)
    {
      if (property == null)
        throw new ArgumentNullException("property");

      // Is the property supported by the list item type?
      if (property is PropertyPathDescriptor)
      {
        var path = (PropertyPathDescriptor) property;
        if (path.ComponentType != typeof (T))
          throw new ArgumentException("Value must be a property of the list item type.", "property");
      }
      else if (!itemProperties.Contains(property))
        throw new ArgumentException("Value must be a property of the list item type.", "property");

      // Is the data type comparable with IComparable or IComparable<T>?
      var propertyType = property.PropertyType;

      // If the property type is nullable, check for IComparable on the nullable base type.
      if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof (Nullable<>))
      {
        var typeParameters = propertyType.GetGenericArguments();
        propertyType = typeParameters[0];
      }

      if (!typeof (IComparable).IsAssignableFrom(propertyType))
      {
        var icomparableOfT = typeof (IComparable<>).MakeGenericType(new[] {propertyType});
        if (!icomparableOfT.IsAssignableFrom(propertyType))
          throw new ArgumentException(string.Format("The property {0} of type {1} does not implement IComparable or IComparable<T>.", property.DisplayName, propertyType));
      }
    }

    private void ApplySortCore()
    {
      if (!suppressUpdates)
      {
        // Invalidate enumerators.
        version++;

        if (SortIndexesDirty)
        {
          RebuildSortIndexes(); // sorts and filters (calls back here to ApplySortCore)
          return;
        }
        else
        {
          lastSortingPropertyIndex = 0;
          GetSortIndexes().Sort(CompareItems);
        }
      }

      QueueEvent(new ListChangedEventArgs(ListChangedType.Reset, -1));
      QueueEvent(new SortEventArgs());
    }

    private bool SortContainsProperty(string propertyName)
    {
      return sortProps.Contains(propertyName);
    }

    private bool IsSortDifferent(SortDescriptionCollection proposedSorts)
    {
      var different = false;

      if (proposedSorts == null || sortProps == null)
        different = (proposedSorts != sortProps);
      else if (proposedSorts.Count == sortProps.Count)
        for (var i = 0; i < proposedSorts.Count; i++)
        {
          if (proposedSorts[i].PropertyName != sortProps[i].PropertyName ||
              proposedSorts[i].Direction != sortProps[i].Direction)
          {
            different = true;
            break;
          }
        }
      else
        different = true;

      return different;
    }

    private int CompareItems(int x, int y)
    {
      var first = list[x];
      var second = list[y];

      int result;

      for (var i = 0; i < sortProps.Count; i++)
      {
        var desc = sortProps[i];
        if (desc != null)
        {
          PropertyDescriptor prop;
          if (desc.PropertyDescriptor != null)
            prop = desc.PropertyDescriptor;
          else
            prop = itemProperties.Find(desc.PropertyName, true);
          if (prop != null)
          {
            object firstValue = null;
            try
            {
              firstValue = prop.GetValue(first);
            }
            catch (TargetException)
            {
              // inaccessible property path
            }
            catch (TargetInvocationException)
            {
              // inaccessible property path
            }

            object secondValue = null;
            try
            {
              secondValue = prop.GetValue(second);
            }
            catch (TargetException)
            {
              // inaccessible property path
            }
            catch (TargetInvocationException)
            {
              // inaccessible property path
            }

            if (firstValue == null || secondValue == null)
              if (firstValue == null)
                if (secondValue == null)
                  result = 0;
                else
                  result = -1;
              else
                result = 1;
            else
              // Use the user-supplied comparer, if any. Will be null after serialization
            if (propertyComparers != null && propertyComparers.ContainsKey(desc.PropertyName))
              result = propertyComparers[desc.PropertyName].Compare(firstValue, secondValue);
            else
              result = ((IComparable) firstValue).CompareTo(secondValue);

            // An inequal key was found.
            if (result != 0)
            {
              if (desc.Direction == ListSortDirection.Descending)
                result *= -1;

              // Record the highest (narrowest) sort property index that has actually affected the sort.
              lastSortingPropertyIndex = Math.Max(lastSortingPropertyIndex, i);

              return result;
            }
          }
        }
      }

      // All keys are equal; return original item order.
      result = x > y ? 1 : (x < y ? -1 : 0);

      // Use the direction of the highest sort property index that has actually affected the sort to determine comparison direction.
      // Note that sortProps.Count == 0 when removing the sort.
      if (sortProps.Count > 0 && sortProps[lastSortingPropertyIndex].Direction == ListSortDirection.Descending)
        result *= -1;

      return result;
    }

    private int GetSortedPositionOfListIndex(int listIndex)
    {
      return GetSortedPositionOfListIndex(listIndex, false);
    }

    private int GetSortedPositionOfListIndex(int listIndex, bool forDelete)
    {
      if (IsSorted || IsFiltered)
        return GetSortIndexes(!forDelete).IndexOf(listIndex);
      else
        return listIndex;
    }

    private int GetListPositionOfViewIndex(int viewIndex)
    {
      if (IsSorted || IsFiltered)
        return GetSortIndexes()[viewIndex];
      else
        return viewIndex;
    }

    private List<int> GetSortIndexes()
    {
      return GetSortIndexes(true);
    }

    private List<int> GetSortIndexes(bool allowRebuild)
    {
      // Items have been added or removed through the list directly, instead of through the ObjectListView.
      if (allowRebuild && SortIndexesDirty)
        RebuildSortIndexes();

      return sortIndexes;
    }

    private void RebuildSortIndexes()
    {
      sortIndexes = new List<int>(list.Count);

      // Note that if indexing is deferred (from BeginUpdate) we keep the sort indexes
      // unsorted and unfiltered, but sized the same as the list.  This allows all of the list
      // elements to be accessed through the view between calls to BeginUpdate and EndUpdate without crashing,
      // but in a deterministic inconsistent state.
      if (!suppressUpdates && IsFiltered)
        for (var i = 0; i < list.Count; i++)
        {
          if (filterPredicate(list[i]))
            sortIndexes.Add(i);
        }
      else
        for (var i = 0; i < list.Count; i++)
          sortIndexes.Add(i);

      TrimSortIndexes();

      if (!suppressUpdates && IsSorted)
        ApplySortCore();
    }

    private void TrimSortIndexes()
    {
      // This is more exact than List<T>.TrimExcess, which only changes the internal array size if the used size is less than 90% of the array size.
      sortIndexes.Capacity = list.Count;
    }

    private bool FilterContainsProperty(string propertyName)
    {
      if (filterProperties != null)
      {
        foreach (PropertyDescriptor property in filterProperties)
        {
          // For a property path, the top-level property name is the one reported in change notifications.
          if (property is PropertyPathDescriptor)
          {
            if (((PropertyPathDescriptor) property).RootPropertyName == propertyName)
              return true;
          }
          else if (property.Name == propertyName)
            return true;
        }

        return false;
      }
      else
        return filterPredicate != null; // user has specified a predicate in code; it's indeterminate whether the predicate uses the property.
    }

    private void QueueEvent(EventArgs eventArgs)
    {
      eventQueue.Enqueue(eventArgs);
    }

    private void RaiseEvents()
    {
      if (!suppressUpdates)
        while (eventQueue.Count > 0)
        {
          var e = eventQueue.Dequeue();
          if (e is ListChangedEventArgs)
          {
            var le = (ListChangedEventArgs) e;
            OnBeforeListChanged(le);
            OnListChanged(le);
            OnAfterListChanged(le);
          }
          else if (e is SortEventArgs)
            OnSorted();
        }
    }

    /// <summary>
    /// Called when an item is added to the list.
    /// </summary>
    /// <remarks>
    /// This method is called when the Add method is called, and also when an item is added through a method of the list
    /// itself (if the list supports the ListChanged event).
    /// </remarks>
    /// <param name="listIndex">Index of the item in the list.</param>
    /// <returns>The index of the item in the sort.</returns>
    private int OnItemAdded(int listIndex)
    {
      // Invalidate enumerators.
      version++;

      // Subscribe to the item change events (either INotifyPropertyChanged or xxxChanged).
      WirePropertyChangedEvents(list[listIndex]);

      // If the add is the result of AddNew(), don't sort.  The item will be put into the sort when the add is committed with
      // EndNew() or IEditableObject.EndEdit().
      int sortedIndex;
      if (IsPendingNewItem(listIndex))
        sortedIndex = AppendNewItemToSortIndexes(listIndex);
      else
      {
        // Sort and filter.
        RebuildSortIndexes();

        sortedIndex = GetSortedPositionOfListIndex(listIndex);
      }

      // If the added item doesn't pass the filter, it isn't visible in the view and shouldn't be reported.
      if (sortedIndex > -1)
        QueueEvent(new ListChangedEventArgs(ListChangedType.ItemAdded, sortedIndex));

      return sortedIndex;
    }

    /// <summary>
    /// Called when an item in the list is changed.
    /// </summary>
    /// <remarks>
    /// This method is called when an item property change is reported by the item's INotifyPropertyChanged.PropertyChanged event
    /// (but not converted to ListChanged by the containing list), and also when an item property change is reported via a
    /// .NET 1.x-style propertyNameChanged event.
    /// </remarks>
    /// <param name="listIndex">Index of the item in the list.</param>
    /// <param name="propertyName">Name of the property changed, or null if the item has been replaced in the list.</param>
    private void OnItemChanged(int listIndex, string propertyName)
    {
      OnItemChanged(listIndex, itemProperties[propertyName]);
    }

    /// <summary>
    /// Called when an item in the list is changed.
    /// </summary>
    /// <remarks>
    /// This method is called when an item is replaced through the indexer, and when a list item change is reported by the
    /// containing list through the ListChanged event.
    /// </remarks>
    /// <param name="listIndex">Index of the item in the list.</param>
    /// <param name="property">The changed property, or null if the item has been replaced in the list.</param>
    private void OnItemChanged(int listIndex, PropertyDescriptor property)
    {
      // Don't re-sort, filter, or raise change notifications for an uncommitted new row.
      if (!IsPendingNewItem(listIndex))
      {
        var wasInFilter = true;
        var isInFilter = true;

        // If item was in filter and still is, it's an ordinary change.
        // If item was in filter and is no longer, it's a delete.
        // If item was not in filter, and now is, it's an add.
        // If item was not in filter and still isn't, ignore.

        if (IsFiltered)
        {
          var originalIndex = GetSortedPositionOfListIndex(listIndex, true);
          wasInFilter = originalIndex > -1;
          isInFilter = filterPredicate(list[listIndex]);

          if (wasInFilter)
          {
            if (!isInFilter)
            {
              OnItemDeleted(originalIndex, listIndex);
              return;
            }
          }
          else if (!isInFilter)
            return;
        }

        OnItemChangedCore(property == null ? null : property.Name);

        var index = GetSortedPositionOfListIndex(listIndex);
        isInFilter = index > -1;

        if (!wasInFilter && isInFilter)
          QueueEvent(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
        else
          QueueEvent(new ListChangedEventArgs(ListChangedType.ItemChanged, index, property));
      }
    }

    private void OnItemChangedCore(string propertyName)
    {
      // Invalidate enumerators only if the property name would affect the contents or order of the list.  Note that
      // propertyName == null when a list item is replaced using the indexer.
      if (propertyName == null || SortContainsProperty(propertyName) || FilterContainsProperty(propertyName))
        version++;

      if (propertyName == null)
      {
        if (IsFiltered)
          ApplyFilter();
        if (IsSorted)
          ApplySortCore();
      }
      else
      {
        if (FilterContainsProperty(propertyName))
          ApplyFilter();
        if (SortContainsProperty(propertyName))
          ApplySortCore();
      }
    }

    /// <summary>
    /// Called when an item is deleted from the list.
    /// </summary>
    /// <remarks>
    /// This method is called when an item is removed using the Remove or RemoveAt methods.  It is also called when an item is
    /// removed from the list through a method of the list itself (if the list supports the ListChanged event).
    /// </remarks>
    /// <param name="viewIndex">Index of the item in the sorted filtered view.</param>
    /// <param name="listIndex">Index of the item in the list.</param>
    private void OnItemDeleted(int viewIndex, int listIndex)
    {
      // The list item was not in the view to begin with.
      if (viewIndex < 0)
        return;

      RemoveItemFromSortIndexes(listIndex);

      version++;

      QueueEvent(new ListChangedEventArgs(ListChangedType.ItemDeleted, viewIndex));

      // The sort indexes remain in the correct sorted order.
    }

    /// <summary>
    /// Called when an item is moved in the list.
    /// </summary>
    /// <remarks>
    /// This method is only called as the result of a call to a method on the underlying list.
    /// </remarks>
    /// <param name="newListIndex">New index of item in the list.</param>
    /// <param name="oldListIndex">Old index of item in the list.</param>
    private void OnItemMoved(int newListIndex, int oldListIndex)
    {
      RebuildSortIndexes();
      version++;

      QueueEvent(new ListChangedEventArgs(ListChangedType.ItemMoved, newListIndex, oldListIndex));
    }

    /// <summary>
    /// Called when the list is reset.
    /// </summary>
    /// <remarks>
    /// This is called when the list is cleared, or when "much of the list has changed".
    /// </remarks>
    private void OnReset()
    {
      RebuildSortIndexes();
      version++;

      QueueEvent(new ListChangedEventArgs(ListChangedType.Reset, -1));
    }

    private void editableListItem_Ended(object sender, EventArgs e)
    {
      if (sender != null && sender == newItemPending)
        FinishAddNew();

      RaiseEvents();
    }

    private void editableListItem_Cancelled(object sender, EventArgs e)
    {
      if (sender != null && sender == newItemPending)
        CancelAddNew();

      RaiseEvents();
    }

    private void listItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      // The only time this event handler is wired up is when the underlying list doesn't correctly report item changes
      // reported by the item INotifyPropertyChanged.PropertyChanged event.
      OnItemChanged(list.IndexOf((T) sender), e.PropertyName);

      RaiseEvents();
    }

    private void list_ListChanged(object sender, ListChangedEventArgs e)
    {
      switch (e.ListChangedType)
      {
        case ListChangedType.ItemAdded: // Update sort and forward event with index of added item in the sort.
          OnItemAdded(e.NewIndex);
          break;

        case ListChangedType.ItemChanged: // Update sort (if sort property has changed) and forward event with index of changed item in the sort.
          OnItemChanged(e.NewIndex, e.PropertyDescriptor);
          break;

        case ListChangedType.ItemDeleted: // Fix up sort indexes and forward event with index of deleted item in the sort.
          OnItemDeleted(GetSortedPositionOfListIndex(e.NewIndex, true), e.NewIndex);
          break;

        case ListChangedType.ItemMoved: // Rebuild sort indexes.
          OnItemMoved(e.NewIndex, e.OldIndex);
          break;

        case ListChangedType.Reset: // Rebuild sort indexes.
          OnReset();
          break;
      }

      RaiseEvents();
    }

    private void propertyComparers_CollectionChanged(object sender, EventArgs e)
    {
      if (IsSorted)
      {
        ApplySortCore();
        RaiseEvents();
      }
    }

    private void RemoveCore(int index)
    {
      if (!allowRemove)
        throw new DataException("AllowRemove is set to false.");

      OnRemovingItem(new RemovingItemEventArgs(index));

      var listIndex = GetListPositionOfViewIndex(index);
      UnwirePropertyChangedEvents(list[listIndex]);
      list.RemoveAt(listIndex);

      // If the list supports ListChanged, OnItemDeleted will have already been called.
      if (!supportsListChanged)
        OnItemDeleted(index, listIndex);
    }

    private void FinishAddNew()
    {
      if (isEditableObject)
      {
        ((IEditableObjectEvents) newItemPending).Ended -= editableListItem_Ended;
        ((IEditableObjectEvents) newItemPending).Cancelled -= editableListItem_Cancelled;
      }

      // Raise the second ListItemChanged / ItemAdded event.
      if (isEditableObject)
      {
        var index = IndexOf(newItemPending.Object);
        QueueEvent(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
      }

      newItemPending = null;

      // Now that the item is committed, reposition it in the sort, and potentially filter it out of the list.
      if (IsFiltered)
        ApplyFilter();
      if (IsSorted)
        ApplySortCore();
    }

    private void CancelAddNew()
    {
      if (isEditableObject)
      {
        ((IEditableObjectEvents) newItemPending).Ended -= editableListItem_Ended;
        ((IEditableObjectEvents) newItemPending).Cancelled -= editableListItem_Cancelled;
      }

      var temp = newItemPending;

      newItemPending = null;

      Remove(temp.Object);
    }

    private bool IsPendingNewItem(int listIndex)
    {
      return (newItemPending != null && list.IndexOf(newItemPending.Object) == listIndex);
    }

    private int AppendNewItemToSortIndexes(int listIndex)
    {
      sortIndexes.Add(listIndex);

      // Keep the size of the sort indexes consistent with the list size.  This avoids automatic rebuilding of the indexes the next
      // time the sort indexes are read.
      TrimSortIndexes();

      // The "sorted" position of the added item (it's not really sorted, it's the position at the end of the view).
      return sortIndexes.Count - 1;
    }

    private void RemoveItemFromSortIndexes(int listIndex)
    {
      sortIndexes.Remove(listIndex);
      for (var i = 0; i < sortIndexes.Count; i++)
      {
        if (sortIndexes[i] > listIndex)
          sortIndexes[i]--;
      }

      // Keep the size of the sort indexes consistent with the list size.  This avoids automatic rebuilding of the indexes the next
      // time the sort indexes are read.
      TrimSortIndexes();
    }
  }
}