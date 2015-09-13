﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.CSharp.RuntimeBinder;
using SD.LLBLGen.Pro.LinqSupportClasses;
using SD.LLBLGen.Pro.ORMSupportClasses;
using Expression = System.Linq.Expressions.Expression;

namespace AW.Helper.LLBL
{
  public static class EntityHelper
  {
    #region Linq

    /// <summary>
    ///   returns the DataSource to use in a Linq query for the entity type specified
    /// </summary>
    /// <typeparam name="T">the type of the entity to get the DataSource for</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="linqMetaData">The Linq meta data.</param>
    /// <returns>the requested DataSource</returns>
    public static DataSourceBase<T> GetQueryableForEntity<T>(this ILinqMetaData linqMetaData, T entity) where T : class, IEntityCore
    {
      return linqMetaData.GetQueryableForEntity(entity.LLBLGenProEntityTypeValue) as DataSourceBase<T>;
    }

    /// <summary>returns the DataSource to use in a Linq query for the entity type specified</summary>
    /// <typeparam name="T">the type of the entity to get the DataSource for</typeparam>
    /// <returns>the requested DataSource</returns>
    public static DataSourceBase<T> GetQueryableForEntity<T>(this ILinqMetaData linqMetaData) where T : class, IEntityCore
    {
      try
      {
        return linqMetaData.GetQueryableForEntity(CreateEntity<T>());
      }
      catch (Exception)
      {
        try
        {
          return GetQueryableForEntitySafely(linqMetaData, typeof (T)) as DataSourceBase<T>;
        }
        catch (Exception)
        {
          return null;
        }
      }
    }

    public delegate IQueryable GetQueryableForEntityDelegate(ILinqMetaData linqMetaData, Type typeOfEntity);

    public static IQueryable GetQueryableForEntityIgnoreIfNull(ILinqMetaData linqMetaData, Type typeOfEntity)
    {
      IQueryable entityQueryable = null;
      if (typeOfEntity != null && linqMetaData != null)
      {
        var dataSource = linqMetaData.GetQueryableForEntity(typeOfEntity);
        entityQueryable = dataSource as IQueryable;
      }
      return entityQueryable;
    }

    public static IDataSource GetQueryableForEntity(this ILinqMetaData linqMetaData, Type typeOfEntity)
    {
      try
      {
        var entityTypeValueForType = GetEntityTypeValueForType(typeOfEntity);
        return linqMetaData.GetQueryableForEntity(entityTypeValueForType);
      }
      catch (Exception)
      {
        try
        {
          return GetQueryableForEntitySafely(linqMetaData, typeOfEntity);
        }
        catch (Exception)
        {
          return null;
        }
      }
    }

    private static IDataSource GetQueryableForEntitySafely(ILinqMetaData linqMetaData, Type typeOfEntity)
    {
      for (var entityTypeValueForType = 0; entityTypeValueForType < Int32.MaxValue; entityTypeValueForType++)
      {
        var queryableForEntity = linqMetaData.GetQueryableForEntity(entityTypeValueForType);
        if (queryableForEntity.ElementType == typeOfEntity)
          return queryableForEntity;
      }
      return null;
    }

    public static IQueryProvider GetProvider(ILinqMetaData linqMetaData)
    {
      return linqMetaData.GetQueryableForEntity(0).Provider;
    }

    public static IQueryable CreateLLBLGenProQueryFromEnumerableExpression(IQueryProvider provider, Expression expression)
    {
      var elementType = expression.Type;
      if (expression.Type.IsGenericType)
      {
        elementType = expression.Type.GetGenericArguments()[0];
      }
      var queryableType = typeof (IQueryable<>).MakeGenericType(new[] {elementType});
      if (queryableType.IsAssignableFrom(expression.Type))
      {
        provider.CreateQuery(expression);
      }
      var enumerableType = typeof (IEnumerable<>).MakeGenericType(new[] {elementType});
      if (!enumerableType.IsAssignableFrom(expression.Type))
      {
        throw new ArgumentException("expression isn't enumerable");
      }
      return (IQueryable) Activator.CreateInstance(typeof (LLBLGenProQuery<>).MakeGenericType(new[] {elementType}), new object[] {provider, expression});
    }

    public static Tuple<Expression, Expression, Expression> GetMethodCallExpressionParts(MethodCallExpression methodCallExpression)
    {
      return GetMethodCallExpressionParts(new Tuple<Expression, Expression, Expression>(null, null, null), methodCallExpression);
    }

    private static Tuple<Expression, Expression, Expression> GetMethodCallExpressionParts(Tuple<Expression, Expression, Expression> result
      , Expression expression)
    {
      var methodCallExpression = expression as MethodCallExpression;
      if (methodCallExpression != null)
      {
        if (methodCallExpression.Method.Name == "Where")
          result = new Tuple<Expression, Expression, Expression>(result.Item1, methodCallExpression.Arguments.Last(), result.Item3);
        if (methodCallExpression.Method.Name == "OrderBy")
          result = new Tuple<Expression, Expression, Expression>(result.Item1, result.Item2, methodCallExpression.Arguments.Last());
        result = methodCallExpression.Arguments.Aggregate(result, GetMethodCallExpressionParts);
      }
      else
      {
        if (expression.NodeType == ExpressionType.Constant)
          result = new Tuple<Expression, Expression, Expression>(expression, result.Item2, result.Item3);
      }
      return result;
    }

    #endregion

    #region CreateEntity

    /// <summary>
    ///   Creates the entity from the .NET type specified.
    /// </summary>
    /// <param name="typeOfEntity">The type of entity.</param>
    /// <returns>An instance of the type.</returns>
    public static IEntityCore CreateEntity(Type typeOfEntity)
    {
      try
      {
        return Activator.CreateInstance(typeOfEntity) as IEntityCore;
      }
      catch (MissingMethodException)
      {
        var elementCreatorCore = CreateElementCreator(typeOfEntity);
        if (elementCreatorCore != null)
          return LinqUtils.CreateEntityInstanceFromEntityType(typeOfEntity, elementCreatorCore);
        throw;
      }
    }

    public static IElementCreatorCore CreateElementCreator(Type typeInTheSameAssemblyAsElementCreator)
    {
      return typeof (IElementCreatorCore).IsAssignableFrom(typeInTheSameAssemblyAsElementCreator)
        ? CreateElementCreatorFromType(typeInTheSameAssemblyAsElementCreator)
        : CreateElementCreator(typeInTheSameAssemblyAsElementCreator.Assembly.GetExportedTypes());
    }

    public static IElementCreatorCore CreateElementCreator(IEnumerable<Type> types)
    {
      var elementCreatorCoreType = typeof (IElementCreatorCore).GetAssignable(types).FirstOrDefault();
      return elementCreatorCoreType == null ? null : CreateElementCreatorFromType(elementCreatorCoreType);
    }

    private static IElementCreatorCore CreateElementCreatorFromType(Type elementCreatorCoreType)
    {
      return Activator.CreateInstance(elementCreatorCoreType) as IElementCreatorCore;
    }

    /// <summary>
    ///   Creates the entity from the .NET type specified.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    /// <returns>An instance of the type.</returns>
    public static T CreateEntity<T>() where T : class, IEntityCore
    {
      return CreateEntity(typeof (T)) as T;
    }

    #endregion

    #region GetFactoryCore

    //public static IEntityFactory GetFactory<T>() where T : EntityBase
    //{
    //  return ((IEntity) CreateEntity<T>()).GetEntityFactory();
    //}

    public static IEntityFactoryCore GetFactoryCore<T>(IElementCreatorCore elementCreatorCore) where T : class, IEntityCore
    {
      return (elementCreatorCore.GetFactory(typeof (T)));
    }

    public static IEntityFactoryCore GetFactoryCore(IEntityCore entity)
    {
      var entity2 = entity as IEntity2;
      if (entity2 == null)
        return ((IEntity) entity).GetEntityFactory();
      return entity2.GetEntityFactory();
    }

    /// <summary>
    ///   Gets the factory of the entity with the .NET type specified
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    /// <returns>factory to use or null if not found</returns>
    public static IEntityFactoryCore GetFactoryCore<T>() where T : class, IEntityCore
    {
      return GetFactoryCore(CreateEntity<T>());
    }

    private static IEntityFactoryCore GetFactoryCore<T>(IEnumerable<T> enumerable) where T : class, IEntityCore
    {
      if (!enumerable.IsNullOrEmpty())
      {
        var first = enumerable.FirstOrDefault(e => e.GetType() == typeof (T)) as IEntityCore;
        if (first != null) return GetFactoryCore(first);
      }
      return GetFactoryCore<T>();
    }

    #endregion

    #region GetEntitiesType

    /// <summary>
    ///   Gets the EntityType value as integer for the entity type passed in
    /// </summary>
    /// <param name="typeOfEntity">The type of entity.</param>
    /// <returns>the EntityType value as integer for the entity type passed in</returns>
    public static int GetEntityTypeValueForType(Type typeOfEntity)
    {
      var entity = CreateEntity(typeOfEntity);
      return entity == null ? 0 : entity.LLBLGenProEntityTypeValue;
    }

    public static int GetEntityTypeValueForType<T>() where T : class, IEntityCore
    {
      return GetEntityTypeValueForType(typeof (T));
    }

    public static IEnumerable<Type> GetEntitiesTypes()
    {
      return MetaDataHelper.GetAllLoadedDescendants(typeof (IEntityCore));
    }

    public static IEnumerable<Type> GetEntitiesTypes(Assembly entityAssembly)
    {
      return typeof (IEntityCore).GetAssignable(entityAssembly.GetExportedTypes());
    }

    public static IEnumerable<Type> GetEntitiesTypes(ILinqMetaData linqMetaData)
    {
      var entitiesTypes = GetEntitiesTypes(linqMetaData.GetType().Assembly);
      if (entitiesTypes.Any())
        return entitiesTypes;
      var topLevelProps =
        from prop in linqMetaData.GetType().GetProperties()
        where prop.PropertyType.IsAssignableTo(typeof (IDataSource)) && prop.PropertyType.IsGenericType
        let typeArgument = prop.PropertyType.GetGenericArguments()[0]
        where typeArgument != null
        select typeArgument;
      return topLevelProps;
    }

    public static IEnumerable<Type> GetEntitiesTypes(Type ancestorType, ILinqMetaData linqMetaData = null)
    {
      if (ancestorType != null)
        return MetaDataHelper.GetDescendants(ancestorType);
      return linqMetaData == null ? GetEntitiesTypes() : GetEntitiesTypes(linqMetaData);
    }

    #endregion

    #region Self Servicing

    /// <summary>
    ///   Converts an entity enumeration to an entity collection. If the enumeration is a ILLBLGenProQuery then executes
    ///   the query this object represents and returns its results in its native container - an entity collection.
    /// </summary>
    /// <typeparam name="T">EntityBase2</typeparam>
    /// <param name="enumerable">The enumerable.</param>
    /// <returns></returns>
    public static EntityCollectionBase<T> ToEntityCollection<T>(this IEnumerable<T> enumerable) where T : EntityBase
    {
      var llblQuery = enumerable as ILLBLGenProQuery;
      if (llblQuery != null)
        return llblQuery.Execute<EntityCollectionBase<T>>();
      var entities = ((IEntityFactory) GetFactoryCore(enumerable)).CreateEntityCollection() as EntityCollectionBase<T>;
      if (entities != null)
        entities.AddRange(enumerable);
      return entities;
    }

    public static IEntityCollection ToEntityCollection(IEnumerable enumerable, Type itemType)
    {
      IEntityCollection entities = null;
      var llblQuery = enumerable as ILLBLGenProQuery;
      if (llblQuery != null)
        entities = llblQuery.Execute() as IEntityCollection;
      if (entities == null)
      {
        var enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
          var firstEntity = ((IEntity) enumerator.Current);
          if (firstEntity == null || !(itemType == firstEntity.GetType())) continue;
          entities = firstEntity.GetEntityFactory().CreateEntityCollection();
          if (entities == null)
            return null;
          foreach (IEntity item in enumerable)
            entities.Add(item);
          break;
        }
      }
      if (entities == null)
      {
        entities = ((IEntity) CreateEntity(itemType)).GetEntityFactory().CreateEntityCollection();
        if (entities == null)
          return null;
        foreach (IEntity item in enumerable)
          entities.Add(item);
      }
      if (entities.Count > 0)
        entities.RemovedEntitiesTracker = entities.EntityFactoryToUse.CreateEntityCollection();
      return entities;
    }

    public static IBindingListView CreateEntityView(IEnumerable enumerable, Type itemType)
    {
      var entityCollection = ToEntityCollection(enumerable, itemType);
      if (entityCollection == null) return null;
      entityCollection.DefaultView.DataChangeAction = PostCollectionChangeAction.NoAction;
      return entityCollection.DefaultView as IBindingListView;
    }

    public static IEntityCollectionCore GetRelatedCollection(IBindingList entityView)
    {
      var view = entityView as IEntityView;
      if (view == null)
      {
        var view2 = entityView as IEntityView2;
        if (view2 != null)
          return view2.RelatedCollection;
      }
      else
        return view.RelatedCollection;
      return null;
    }

    /// <summary>
    ///   Gets the entity field from the name of the field.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>the entity field</returns>
    public static IEntityField GetFieldFromFieldName(IEntity entity, string fieldName)
    {
      return entity.Fields[fieldName] ?? entity.Fields.Cast<IEntityField>().FirstOrDefault(ef => ef.Name.Equals(fieldName, StringComparison.InvariantCultureIgnoreCase));
    }

    public static IEnumerable<IEntityCore> AsEnumerable(this IEntityCollectionCore entityCollection)
    {
      return entityCollection.Cast<IEntityCore>();
    }

    /// <summary>
    ///   Gets a entity field enumeration from entity fields.
    /// </summary>
    /// <param name="entityFields">The entity fields.</param>
    /// <returns>entity field enumeration</returns>
    public static IEnumerable<IEntityField> AsEnumerable(this IEntityFields entityFields)
    {
      return entityFields.Cast<IEntityField>();
    }

    /// <summary>
    ///   Gets a entity field enumeration from entity fields.
    /// </summary>
    /// <param name="entityFields">The entity fields.</param>
    /// <returns>entity field enumeration</returns>
    public static IEnumerable<IEntityField2> AsEnumerable(this IEntityFields2 entityFields)
    {
      return entityFields.Cast<IEntityField2>();
    }

    public static IEnumerable<IEntityFieldCore> GetFields(this IEntityCore entityCore)
    {
      return entityCore.Fields;
    }

    public static IEnumerable<IEntityFieldCore> GetChangedFields(this IEntityCore entity)
    {
      return entity.IsDirty ? GetFields(entity).Where(f => f.IsChanged) : Enumerable.Empty<IEntityFieldCore>();
    }

    public static void RevertChangesToDBValue(this IEntityCore entity)
    {
      foreach (var changedField in entity.GetChangedFields())
        changedField.ForcedCurrentValueWrite(changedField.DbValue, changedField.DbValue);
      entity.IsDirty = false;
    }

    public static void RevertChangesToDBValue(this IEntityCollection entityCollection)
    {
      foreach (var dirtyEntity in entityCollection.DirtyEntities)
        dirtyEntity.RevertChangesToDBValue();
      var newEntities = entityCollection.Cast<IEntity>().Where(e => e.IsNew).ToList();
      foreach (var newEntity in newEntities)
        entityCollection.Remove(newEntity);
    }

    public static void RevertChangesToDBValue(this IEntityCollection2 entityCollection)
    {
      foreach (var dirtyEntity in entityCollection.DirtyEntities)
        dirtyEntity.RevertChangesToDBValue();
      var newEntities = entityCollection.Cast<IEntity2>().Where(e => e.IsNew).ToList();
      foreach (var newEntity in newEntities)
        entityCollection.Remove(newEntity);
    }

    public static void RevertChangesToDBValue(IEntityCollectionCore entityCollection)
    {
      var collection = entityCollection as IEntityCollection;
      if (collection == null)
      {
        var entityCollection2 = entityCollection as IEntityCollection2;
        if (entityCollection2 != null)
          entityCollection2.RevertChangesToDBValue();
      }
      else
        collection.RevertChangesToDBValue();
    }

    public static void RevertChangesToDBValue<T>(IEnumerable<T> entities) where T : class, IEntityCore
    {
      foreach (var dirtyEntity in entities.Where(e => e.IsDirty))
        dirtyEntity.RevertChangesToDBValue();
    }

    public static void RevertChangesToDBValue(IEnumerable modifiedEntities)
    {
      var postCollectionChangeActionNoAction = false;
      IEntityView2 view2 = null;
      var view = modifiedEntities as IEntityView;
      if (view == null)
        view2 = modifiedEntities as IEntityView2;
      if (view == null && view2 == null)
      {
        dynamic bindingListView = modifiedEntities as IBindingListView;
        if (bindingListView != null)
          try
          {
            modifiedEntities = bindingListView.List;
            view = modifiedEntities as IEntityView;
            view2 = modifiedEntities as IEntityView2;
          }
          catch (RuntimeBinderException)
          {
            //  MyProperty doesn't exist
          }
      }
      if (view != null)
      {
        modifiedEntities = ((IEntityView) modifiedEntities).RelatedCollection;
        postCollectionChangeActionNoAction = view.DataChangeAction == PostCollectionChangeAction.NoAction;
      }
      if (view2 != null)
      {
        modifiedEntities = ((IEntityView2) modifiedEntities).RelatedCollection;
        postCollectionChangeActionNoAction = view2.DataChangeAction == PostCollectionChangeAction.NoAction;
      }
      var entities = modifiedEntities as IEntityCollectionCore;
      if (entities != null)
      {
        var entityCollection = entities;
        if (entityCollection.RemovedEntitiesTracker != null)
        {
          if (postCollectionChangeActionNoAction)
          {
            if (view != null) view.DataChangeAction = PostCollectionChangeAction.ReapplyFilterAndSorter;
            if (view2 != null) view2.DataChangeAction = PostCollectionChangeAction.ReapplyFilterAndSorter;
          }
          try
          {
            var collection = modifiedEntities as IEntityCollection;
            if (collection == null)
            {
              var entityCollection2 = modifiedEntities as IEntityCollection2;
              if (entityCollection2 != null)
                entityCollection2.AddRange((IEntityCollection2) entityCollection.RemovedEntitiesTracker);
            }
            else
              collection.AddRange((IEntityCollection) entityCollection.RemovedEntitiesTracker);
          }
          finally
          {
            if (postCollectionChangeActionNoAction)
            {
              if (view != null) view.DataChangeAction = PostCollectionChangeAction.NoAction;
              if (view2 != null) view2.DataChangeAction = PostCollectionChangeAction.NoAction;
            }
          }
        }
        RevertChangesToDBValue(entityCollection);
        if (entityCollection.RemovedEntitiesTracker != null)
          entityCollection.RemovedEntitiesTracker.Clear();
      }
      else
      {
        RevertChangesToDBValue(modifiedEntities.Cast<IEntityCore>());
      }
    }

    public static void Undo(object modifiedData)
    {
      var listItemType = GetListItemType(modifiedData);
      if (IsEntityCore(listItemType))
      {
        var enumerable = modifiedData as IEnumerable;
        if (enumerable == null)
        {
          var entity = modifiedData as IEntity;
          if (entity != null)
            RevertChangesToDBValue(entity);
          else
            RevertChangesToDBValue((IEntity2) modifiedData);
        }
        else
          RevertChangesToDBValue(enumerable);
      }
    }

    private static Type GetListItemType(object modifiedData)
    {
      return ListBindingHelper.GetListItemType(modifiedData);
    }

    /// <summary>
    ///   Gets the factory of the entity with the .NET type specified
    /// </summary>
    /// <returns>factory to use or null if not found</returns>
    /// <summary>
    ///   Deletes the entities.
    /// </summary>
    /// <param name="entitiesToDelete">The entities to delete.</param>
    /// <returns></returns>
    public static int DeleteEntities(IEnumerable entitiesToDelete)
    {
      var entityCollectionBase = entitiesToDelete as EntityCollectionBase<EntityBase>;
      if (entityCollectionBase != null)
        return entityCollectionBase.DeleteMulti();
      return entitiesToDelete.Cast<EntityBase>().Count(entity => entity.Delete() || entity.IsNew);
    }

    /// <summary>
    ///   Deletes the specified data from the DB.
    /// </summary>
    /// <param name="dataToDelete">The data to delete.</param>
    /// <returns></returns>
    public static int Delete(object dataToDelete)
    {
      var listItemType = GetListItemType(dataToDelete);
      if (typeof (IEntity).IsAssignableFrom(listItemType))
      {
        var enumerable = dataToDelete as IEnumerable;
        return enumerable == null ? Convert.ToInt32(((IEntity) dataToDelete).Delete()) : DeleteEntities(enumerable);
      }
      return 0;
    }

    /// <summary>
    ///   Saves all dirty objects inside the enumeration passed to the persistent storage.
    /// </summary>
    /// <param name="entitiesToSave">The entities to save.</param>
    /// <returns>the amount of persisted entities</returns>
    public static int SaveEntities(IEnumerable entitiesToSave)
    {
      if (entitiesToSave is IEntityView)
        entitiesToSave = ((IEntityView) entitiesToSave).RelatedCollection;
      var collection = entitiesToSave as IEntityCollection;
      if (collection != null)
      {
        var entityCollection = collection;
        var modifyCount = 0;
        if (entityCollection.RemovedEntitiesTracker != null)
          modifyCount = entityCollection.RemovedEntitiesTracker.DeleteMulti();
        return modifyCount + entityCollection.SaveMulti();
      }
      return entitiesToSave.Cast<EntityBase>().Count(entity => entity.IsDirty && entity.Save());
    }

    /// <summary>
    ///   Saves any changes to the specified data to the DB.
    /// </summary>
    /// <param name="dataToSave">The data to save, must be a CommonEntityBase or a list of CommonEntityBase's.</param>
    /// <returns>The number of persisted entities.</returns>
    public static int Save(object dataToSave)
    {
      var listItemType = GetListItemType(dataToSave);
      if (typeof (IEntity).IsAssignableFrom(listItemType))
      {
        var enumerable = dataToSave as IEnumerable;
        return enumerable == null ? Convert.ToInt32(((IEntity) dataToSave).Save()) : SaveEntities(enumerable);
      }
      return 0;
    }

    public static void SetSelfservicingConnectionString(Type commonDaoBaseType, object context, string connectionString)
    {
      if (!String.IsNullOrEmpty(connectionString))
      {
        var actualConnectionStringField = commonDaoBaseType.GetField("ActualConnectionString");
        if (actualConnectionStringField == null)
        {
          throw new InvalidOperationException(String.Format("The type '{0}' doesn't have a static property ActualConnectionString.", commonDaoBaseType.FullName));
        }
        //CustomCxString overrides config value
        actualConnectionStringField.SetValue(context, connectionString);
      }
    }

    #endregion

    #region Adapter

    /// <summary>
    ///   Saves all dirty objects inside the enumeration passed to the persistent storage.
    /// </summary>
    /// <param name="entitiesToSave">The entities to save.</param>
    /// <param name="dataAccessAdapter">The data access adapter.</param>
    /// <returns>the amount of persisted entities</returns>
    public static int SaveEntities(IEnumerable entitiesToSave, IDataAccessAdapter dataAccessAdapter)
    {
      if (entitiesToSave is IEntityView2)
        entitiesToSave = ((IEntityView2) entitiesToSave).RelatedCollection;
      var collectionToSave = entitiesToSave as IEntityCollection2;
      if (collectionToSave != null)
      {
        var entityCollection = collectionToSave;
        var modifyCount = 0;
        if (entityCollection.RemovedEntitiesTracker != null)
          modifyCount = dataAccessAdapter.DeleteEntityCollection(entityCollection.RemovedEntitiesTracker);
        return modifyCount + dataAccessAdapter.SaveEntityCollection(collectionToSave,false, true);
      }
      return SaveEntities(entitiesToSave.Cast<IEntity2>(), dataAccessAdapter);
    }

    /// <summary>
    ///   Saves all dirty objects inside the enumeration passed to the persistent storage.
    /// </summary>
    /// <param name="entitiesToSave">The entities to save.</param>
    /// <param name="dataAccessAdapter">The data access adapter.</param>
    /// <returns>the amount of persisted entities</returns>
    public static int SaveEntities(IEnumerable<IEntity2> entitiesToSave, IDataAccessAdapter dataAccessAdapter)
    {
      return entitiesToSave.Count(entity => entity.IsDirty && dataAccessAdapter.SaveEntity(entity));
    }

    /// <summary>
    ///   Saves any changes to the specified data to the DB.
    /// </summary>
    /// <param name="dataToSave">The data to save, must be a CommonEntityBase or a list of CommonEntityBase's.</param>
    /// <param name="dataAccessAdapter">The data access adapter.</param>
    /// <returns>The number of persisted entities.</returns>
    public static int Save(object dataToSave, IDataAccessAdapter dataAccessAdapter)
    {
      var listItemType = GetListItemType(dataToSave);
      if (typeof (IEntity2).IsAssignableFrom(listItemType))
      {
        var enumerable = dataToSave as IEnumerable;
        return enumerable == null ? Convert.ToInt32(dataAccessAdapter.SaveEntity((IEntity2) dataToSave)) : SaveEntities(enumerable, dataAccessAdapter);
      }
      return 0;
    }

    /// <summary>
    ///   Deletes all entities inside the enumeration passed from the DB.
    /// </summary>
    /// <param name="entitiesToDelete">The entities to delete.</param>
    /// <param name="dataAccessAdapter">The data access adapter.</param>
    /// <returns>the amount of persisted entities</returns>
    public static int DeleteEntities(IEnumerable<IEntity2> entitiesToDelete, IDataAccessAdapter dataAccessAdapter)
    {
      return entitiesToDelete.Count(dataAccessAdapter.DeleteEntity);
    }

    /// <summary>
    ///   Deletes the entities.
    /// </summary>
    /// <param name="entitiesToDelete">The entities to delete.</param>
    /// <param name="dataAccessAdapter">The data access adapter.</param>
    /// <returns></returns>
    public static int DeleteEntities(IEnumerable entitiesToDelete, IDataAccessAdapter dataAccessAdapter)
    {
      var collectionToDelete = entitiesToDelete as IEntityCollection2;
      if (collectionToDelete != null)
        return dataAccessAdapter.DeleteEntityCollection(collectionToDelete);
      return DeleteEntities(entitiesToDelete.Cast<IEntity2>(), dataAccessAdapter);
    }

    public static int Delete(object dataToDelete, IDataAccessAdapter dataAccessAdapter)
    {
      var listItemType = GetListItemType(dataToDelete);
      if (typeof (IEntity2).IsAssignableFrom(listItemType))
      {
        var enumerable = dataToDelete as IEnumerable;
        return enumerable == null ? Convert.ToInt32(dataAccessAdapter.DeleteEntity((IEntity2) dataToDelete)) : DeleteEntities(enumerable, dataAccessAdapter);
      }
      return 0;
    }

    #region GetDataAccessAdapter

    /// <summary>
    ///   Gets the data access adapter from a DataSource2.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="query">The query.</param>
    /// <returns></returns>
    public static IDataAccessAdapter GetDataAccessAdapter<T>(DataSource2<T> query) where T : EntityBase2
    {
      return query == null ? null : GetDataAccessAdapter(query.Provider);
    }

    /// <summary>
    ///   Gets the data access adapter from a query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns></returns>
    public static IDataAccessAdapter GetDataAccessAdapter(IQueryable query)
    {
      return GetDataAccessAdapter(query.Provider);
    }

    public static IDataAccessAdapter GetDataAccessAdapter(IEnumerable enumerable)
    {
      var queryable = enumerable as IQueryable;
      return queryable == null ? null : GetDataAccessAdapter(queryable);
    }

    /// <summary>
    ///   Gets the data access adapter from a LLBLGenProProvider2.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns></returns>
    public static IDataAccessAdapter GetDataAccessAdapter(IQueryProvider provider)
    {
      var llblGenProProvider2 = provider as LLBLGenProProvider2;
      return llblGenProProvider2 == null ? null : llblGenProProvider2.AdapterToUse;
    }

    /// <summary>
    ///   Gets the IDataAccessAdapter from a ILinqMetaData Via the provider.
    /// </summary>
    /// <param name="linqMetaData">The ILinqMetaData.</param>
    /// <returns></returns>
    public static IDataAccessAdapter GetDataAccessAdapter(ILinqMetaData linqMetaData)
    {
      return GetDataAccessAdapter(GetProvider(linqMetaData));
    }

    /// <summary>
    ///   Gets the adapter from the ILinqMetaData.
    /// </summary>
    /// <param name="linqMetaData">The linq meta data.</param>
    /// <returns></returns>
    public static DataAccessAdapterBase GetDataAccessAdapterViaReflection(ILinqMetaData linqMetaData)
    {
      var adapterToUseProperty = linqMetaData.GetType().GetProperty("AdapterToUse");
      if (adapterToUseProperty != null)
        return adapterToUseProperty.GetValue(linqMetaData, null) as DataAccessAdapterBase;
      return null;
    }

    public static IDataAccessAdapter GetDataAccessAdapter(ILinqMetaData linqMetaData, bool isAdapter)
    {
      IDataAccessAdapter adapter = null;
      if (isAdapter)
      {
        dynamic dlinqMetaData = linqMetaData;
        if (dlinqMetaData != null) adapter = dlinqMetaData.AdapterToUse;
      }
      return adapter;
    }

    #endregion

    #region ToEntityCollection2

    /// <summary>
    ///   Converts an entity enumeration to an entity collection. If the enumeration is a ILLBLGenProQuery then executes
    ///   the query this object represents and returns its results in its native container - an entity collection.
    /// </summary>
    /// <typeparam name="T">EntityBase2</typeparam>
    /// <param name="enumerable">The enumerable.</param>
    /// <returns>
    ///   Results of the query in an entity collection.
    /// </returns>
    public static EntityCollectionBase2<T> ToEntityCollection2<T>(this IEnumerable<T> enumerable) where T : EntityBase2
    {
      var entityCollection = enumerable as EntityCollectionBase2<T>;
      if (entityCollection != null)
        return entityCollection;
      var llblQuery = enumerable as ILLBLGenProQuery;
      return llblQuery == null ? ToEntityCollection(enumerable, ((IEntityFactory2) GetFactoryCore(enumerable))) : llblQuery.Execute<EntityCollectionBase2<T>>();
    }

    private static EntityCollectionBase2<T> ToEntityCollection<T>(IEnumerable<T> enumerable, IEntityFactory2 entityFactory) where T : EntityBase2
    {
      var entityCollection = entityFactory.CreateEntityCollection() as EntityCollectionBase2<T>;
      if (entityCollection != null)
      {
        entityCollection.AddRange(enumerable);
        return entityCollection;
      }
      return null;
    }

    public static IEntityCollection2 ToEntityCollection2(IEnumerable enumerable, Type itemType)
    {
      IEntityCollection2 entities = null;
      var llblQuery = enumerable as ILLBLGenProQuery;
      if (llblQuery != null)
        entities = llblQuery.Execute() as IEntityCollection2;
      if (entities == null)
      {
        var enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
          var firstEntity = ((IEntity2) enumerator.Current);
          if (firstEntity == null || !(itemType == firstEntity.GetType())) continue;
          entities = firstEntity.GetEntityFactory().CreateEntityCollection();
          if (entities == null)
            return null;
          foreach (IEntity2 item in enumerable)
            entities.Add(item);
          break;
        }
      }
      if (entities == null)
      {
        entities = ((IEntity2) CreateEntity(itemType)).GetEntityFactory().CreateEntityCollection();
        if (entities == null)
          return null;
        foreach (IEntity2 item in enumerable)
          entities.Add(item);
      }
      if (entities.Count > 0)
        entities.RemovedEntitiesTracker = entities.EntityFactoryToUse.CreateEntityCollection();
      return entities;
    }

    #endregion

    public static IBindingListView CreateEntityView2(IEnumerable enumerable, Type itemType)
    {
      var entityCollection = ToEntityCollection2(enumerable, itemType);
      if (entityCollection == null) return null;
      entityCollection.DefaultView.DataChangeAction = PostCollectionChangeAction.NoAction;
      return entityCollection.DefaultView as IBindingListView;
    }

    public static IRelationPredicateBucket GetRelationInfo(IEntity2 entity, string fieldName, IEnumerable<string> primaryKeyColumnNames)
    {
      var bucket = new RelationPredicateBucket();
      bucket.Relations.AddRange(entity.GetRelationsForFieldOfType(fieldName));
      foreach (var entityField2 in primaryKeyColumnNames.Select(primaryKeyColumn => entity.Fields[primaryKeyColumn]))
        bucket.PredicateExpression.Add(new FieldCompareValuePredicate(entityField2, null, ComparisonOperator.Equal, entityField2.CurrentValue, entity.LLBLGenProEntityName + "__"));
      return bucket;
    }

    public static IRelationPredicateBucket GetRelationInfo(IEntity2 entity, string fieldName)
    {
      var bucket = new RelationPredicateBucket();
      bucket.Relations.AddRange(entity.GetRelationsForFieldOfType(fieldName));
      foreach (var entityField2 in entity.PrimaryKeyFields)
        bucket.PredicateExpression.Add(new FieldCompareValuePredicate(entityField2, null, ComparisonOperator.Equal, entityField2.CurrentValue, entity.LLBLGenProEntityName + "__"));
      return bucket;
    }

    #endregion

    /// <summary>
    ///   Gets the properties of type entity since sometimes these properties are not browseable so they need to be handled as
    ///   a special case.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="includeGenericParameters">if set to <c>true</c> [include generic].</param>
    /// <returns></returns>
    public static IEnumerable<PropertyDescriptor> GetPropertiesOfTypeEntity(Type type, bool includeGenericParameters = false)
    {
      return MetaDataHelper.GetPropertyDescriptors(type).FilterByIsEntityCore(true, includeGenericParameters);
    }

    public static IEnumerable<PropertyDescriptor> FilterByIsEntityCore(this IEnumerable<PropertyDescriptor> propertyDescriptors, bool? isEntityCore = true, bool includeGenericParameters = false)
    {
      return isEntityCore.HasValue ? propertyDescriptors.Where(propertyDescriptor => IsEntityCore(propertyDescriptor, includeGenericParameters) == isEntityCore.Value) : propertyDescriptors;
    }

    public static bool IsEntityCore(PropertyDescriptor propertyDescriptor, bool includeGenericParameters = false)
    {
      return IsEntityCore(propertyDescriptor.PropertyType, includeGenericParameters);
    }

    public static bool IsMemberOfEntityCore(PropertyDescriptor propertyDescriptor)
    {
      return IsEntityCore(propertyDescriptor.ComponentType);
    }

    public static bool IsEntityCore(Type type, bool includeGenericParameters = false)
    {
      var isEntityCore = typeof (IEntityCore).IsAssignableFrom(type);
      if (isEntityCore || !includeGenericParameters) return isEntityCore;
      var typeParametersOfGenericType = MetaDataHelper.GetTypeParametersOfGenericType(type);
      return typeParametersOfGenericType != null && typeParametersOfGenericType.Any(t => IsEntityCore(t));
    }

    public static IEntityFields GetFieldsFromType(Type type)
    {
      var ef = CreateEntity(type) as IEntity;
      //			return ef.Create().Fields;
      return ef == null ? null : ef.Fields;
    }

    public static IEntityFields2 GetFieldsFromType2(Type type)
    {
      var ef = CreateEntity(type) as IEntity2;
      //			return ef.Create().Fields;
      return ef == null ? null : ef.Fields;
    }

    public static IEnumerable<PropertyDescriptor> GetNavigatorProperties(IEntityCore entityCore)
    {
      return MetaDataHelper.GetPropertyDescriptors(entityCore.GetType()).FilterByIsNavigator(entityCore);
    }

    public static IEnumerable<PropertyDescriptor> FilterByIsNavigator(this IEnumerable<PropertyDescriptor> propertyDescriptors, IEntityCore entityCore)
    {
      return propertyDescriptors.Where(propertyDescriptor => FieldIsNavigator(entityCore, propertyDescriptor.Name) && IsMemberOfEntityCore(propertyDescriptor));
    }

    public static IEnumerable<PropertyDescriptor> FilterByIsField(this IEnumerable<PropertyDescriptor> propertyDescriptors, IEntityCore entityCore)
    {
      var propertyNames = entityCore.Fields.Select(ef => ef.Name);
      return propertyDescriptors.Where(propertyDescriptor => propertyNames.Contains(propertyDescriptor.Name));
    }

    public static bool FieldIsNavigator(IEntityCore entityCore, string fieldName)
    {
      var relations = entityCore.GetRelationsForFieldOfType(fieldName);
      return relations.Count > 0;
    }

    public static string GetNameFromEntity(IEntityCore entity)
    {
      return GetNameFromEntityName(entity.LLBLGenProEntityName);
    }

    public static string GetNameFromEntityEnum(Enum entity)
    {
      return GetNameFromEntityName(entity.ToString());
    }

    public static string GetNameFromEntityName(string llblGenProEntityName)
    {
      return llblGenProEntityName.Replace("Entity", "");
    }

    #region GetFieldPersistenceInfo

    public static IFieldPersistenceInfo GetFieldPersistenceInfo(IDataAccessAdapter dataAccessAdapter, IEntityField2 field)
    {
      var fullListQueryMethod = dataAccessAdapter.GetType().GetMethod("GetFieldPersistenceInfo", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] {typeof (IEntityField2)}, null);
      return fullListQueryMethod.Invoke(dataAccessAdapter, new[] {field}) as IFieldPersistenceInfo;
    }

    public static IFieldPersistenceInfo GetFieldPersistenceInfoSafely(IDataAccessAdapter dataAccessAdapter, IEntityField2 field)
    {
      IFieldPersistenceInfo fieldPersistenceInfo = null;
      try
      {
        fieldPersistenceInfo = GetFieldPersistenceInfo(dataAccessAdapter, field);
      }
      catch (Exception e)
      {
        e.TraceOut();
      }
      return fieldPersistenceInfo;
    }

    public static IFieldPersistenceInfo GetFieldPersistenceInfo(IEntityFieldCore field, IDataAccessAdapter adapter = null)
    {
      var entityField = field as IEntityField;
      if (entityField != null)
        return entityField;
      return adapter == null ? null : GetFieldPersistenceInfo(adapter, (IEntityField2) field);
    }

    public static IFieldPersistenceInfo GetFieldPersistenceInfoSafely(IEntityFieldCore field, IDataAccessAdapter adapter = null)
    {
      var entityField = field as IEntityField;
      if (entityField != null)
        return entityField;
      return adapter == null ? null : GetFieldPersistenceInfoSafely(adapter, (IEntityField2) field);
    }

    public static IFieldPersistenceInfo GetFieldPersistenceInfoSafely(IEntityCore entity, IDataAccessAdapter adapter = null)
    {
      return GetFieldPersistenceInfoSafely(entity.Fields.First(), adapter);
    }

    #endregion

    public static IEnumerable<string> GetFieldsCustomProperties(IEntityCore entity, string fieldName)
    {
      return entity.FieldsCustomPropertiesOfType.ContainsKey(fieldName)
        ? entity.FieldsCustomPropertiesOfType[fieldName].Values
        : Enumerable.Empty<string>();
    }

    /// <summary>
    ///   Gets the navigator name(s) for a foreign key field.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns></returns>
    public static IEnumerable<string> GetNavigatorNames(IEntityCore entity, string fieldName)
    {
      return from entityRelation in entity.GetAllRelations().Where(r => !r.StartEntityIsPkSide)
        from fkEntityFieldCoreObject in entityRelation.GetAllFKEntityFieldCoreObjects()
        where fkEntityFieldCoreObject.Name.Equals(fieldName)
        select entityRelation.MappedFieldName;
    }
  }

  public enum LLBLQueryType
  {
    Native,
    QuerySpec,
    Linq
  }
}