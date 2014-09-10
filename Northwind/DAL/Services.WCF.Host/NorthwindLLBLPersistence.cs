﻿using System;
using System.Linq;
using AW.Helper.LLBL;
using Northwind.DAL.Interfaces;
using Northwind.DAL.Linq;
using SD.LLBLGen.Pro.ORMSupportClasses;

namespace Northwind.DAL.Services.WCF.Host
{
  public class NorthwindLLBLPersistence:LinqMetaData , ILLBLPersistance
  {
    private DataAccessAdapterBase _adapterToUse;
    
    public void Commit(IUnitOfWorkCore uow)
    {
      using (var adapter = Factories.CreateDataAccessAdapter())
        uow.Commit(adapter);
    }

    public IQueryable GetQueryableForEntity(Type entityType)
    {
      var linqMetaData = new LinqMetaData(GetAdapter());
      return (IQueryable) linqMetaData.GetQueryableForEntity(entityType);
    }

    private DataAccessAdapterBase GetAdapter()
    {
      return _adapterToUse = _adapterToUse ?? Factories.CreateDataAccessAdapter();
    }
  }
}