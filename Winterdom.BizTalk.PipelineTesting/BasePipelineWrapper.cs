
//
// BasePipelineWrapper.cs
//
// Author:
//    Tomas Restrepo (tomasr@mvps.org)
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.BizTalk.PipelineOM;
using Microsoft.BizTalk.Message.Interop;
using Microsoft.BizTalk.Component.Interop;
using Microsoft.XLANGs.BaseTypes;
using IPipeline = Microsoft.Test.BizTalk.PipelineObjects.IPipeline;
using PStage = Microsoft.Test.BizTalk.PipelineObjects.Stage;


namespace Winterdom.BizTalk.PipelineTesting
{
   /// <summary>
   /// Wrapper around a pipeline you can execute
   /// </summary>
   public abstract class BasePipelineWrapper : IEnumerable<IBaseComponent>
   {
      private IPipeline _pipeline;
      private IPipelineContext _pipelineContext;
      private bool _isReceivePipeline;

      #region Properties
      //
      // Properties
      //

      internal IPipeline Pipeline
      {
         get { return _pipeline; }
      }
      internal IPipelineContext Context
      {
         get { return _pipelineContext; }
      }

      /// <summary>
      /// Gets or Set the thumbprint for the Group
      /// Signing Certificate. Null by default
      /// </summary>
      public string GroupSigningCertificate
      {
         get { return _pipelineContext.GetGroupSigningCertificate(); }
         set
         {
            IConfigurePipelineContext ctxt = (IConfigurePipelineContext)_pipelineContext;
            ctxt.SetGroupSigningCertificate(value);
         }
      }

      #endregion // Properties

      /// <summary>
      /// Initializes an instance
      /// </summary>
      /// <param name="pipeline">Pipeline object to wrap</param>
      /// <param name="isReceivePipeline">True if it's a receive pipeline</param>
      protected BasePipelineWrapper(IPipeline pipeline, bool isReceivePipeline)
      {
         if ( pipeline == null )
            throw new ArgumentNullException("pipeline");
         _pipeline = pipeline;
         _pipelineContext = CreatePipelineContext();
         _isReceivePipeline = isReceivePipeline;
      }

      /// <summary>
      /// Adds a component to the specified stage
      /// </summary>
      /// <param name="component">Component to add to the stage</param>
      /// <param name="stage">Stage to add it to</param>
      public void AddComponent(IBaseComponent component, PipelineStage stage)
      {
         if ( component == null )
            throw new ArgumentNullException("component");
         if ( stage == null )
            throw new ArgumentNullException("stage");

         if ( stage.IsReceiveStage != _isReceivePipeline )
            throw new ArgumentException("Invalid Stage", "stage");

         PStage theStage = FindStage(stage);
         theStage.AddComponent(component);
      }

      /// <summary>
      /// Adds a new document specification to the list
      /// of Known Schemas for this pipeline.
      /// </summary>
      /// <remarks>
      /// Adding known schemas is necessary so that
      /// document type resolution works in the disassembler/assembler
      /// stages
      /// </remarks>
      /// <param name="schemaType">Type of the document schema to add</param>
      public void AddDocSpec(Type schemaType)
      {
         if ( schemaType == null )
            throw new ArgumentNullException("schemaType");

         DocSpecLoader loader = new DocSpecLoader();
         IConfigurePipelineContext ctxt = (IConfigurePipelineContext)Context;

         Type[] roots = GetSchemaRoots(schemaType);
         foreach ( Type root in roots )
         {
            IDocumentSpec docSpec = loader.LoadDocSpec(root);
            ctxt.AddDocSpecByType(GetSchemaRoot(root), docSpec);
            // bit of a hack: we add both the assembly qualified name
            // as well as the full name (no assembly). This gets
            // around the issue where pipelines referencing local
            // assemblies don't have the fully qualified name
            ctxt.AddDocSpecByName(root.AssemblyQualifiedName, docSpec);
            ctxt.AddDocSpecByName(root.FullName, docSpec);
         }
      }

      /// <summary>
      /// Returns the document spec object for a known doc
      /// spec given the fully qualified type name
      /// </summary>
      /// <param name="name">Typename of the schema</param>
      /// <returns>The docSpec object</returns>
      public IDocumentSpec GetKnownDocSpecByName(string name)
      {
         return Context.GetDocumentSpecByName(name);
      }

      /// <summary>
      /// Returns the document spec object for a known doc
      /// spec given the name of the root (namespace#root)
      /// </summary>
      /// <param name="name">Name of the root</param>
      /// <returns>The docSpec object</returns>
      public IDocumentSpec GetKnownDocSpecByType(string name)
      {
         return Context.GetDocumentSpecByType(name);
      }

      /// <summary>
      /// Enables transactional support for the pipeline
      /// execution, so that the pipeline context
      /// returns a valid transaction
      /// </summary>
      /// <returns>An object to control the transaction lifetime and result</returns>
      public TransactionControl EnableTransactions()
      {
         IConfigurePipelineContext ctxt = (IConfigurePipelineContext)_pipelineContext;
         return ctxt.EnableTransactionSupport();
      }

      #region Protected Methods
      //
      // Protected Methods
      //

      /// <summary>
      /// Finds a stage in the pipeline
      /// </summary>
      /// <param name="stage">Stage definition</param>
      /// <returns>The stage, if found, or a new stage if necessary</returns>
      protected PStage FindStage(PipelineStage stage)
      {
         PStage theStage = null;
         foreach ( PStage pstage in _pipeline.Stages )
         {
            if ( pstage.Id == stage.ID )
            {
               theStage = pstage;
               break;
            }
         }
         if ( theStage == null )
         {
            theStage = new PStage(stage.Name, stage.ExecuteMethod, stage.ID, _pipeline);
            _pipeline.Stages.Add(theStage);
         }
         return theStage;
      }

      /// <summary>
      /// Creates a new pipeline context for the execution
      /// </summary>
      /// <returns>The new pipeline context.</returns>
      protected IPipelineContext CreatePipelineContext()
      {
         return new PipelineContext();
      }

      #endregion // Protected Methods

      #region Private Methods
      //
      // Private Methods
      //

      /// <summary>
      /// Gets the namespace#root name for a schema.
      /// If the schema has multiple roots, all are returned.
      /// </summary>
      /// <param name="schemaType">Type of the schema</param>
      /// <returns>Roots of the schema</returns>
      private Type[] GetSchemaRoots(Type schemaType)
      {
         string root = GetSchemaRoot(schemaType);
         if ( root != null )
         {
            return new Type[] { schemaType };
         } else
         {
            Type[] rts = schemaType.GetNestedTypes();
            return rts;
         }
      }

      /// <summary>
      /// Gets the root name (namespace#root) for a schema type
      /// </summary>
      /// <param name="schemaType">Type of the schema</param>
      /// <returns>Roots of the schema</returns>
      private string GetSchemaRoot(Type schemaType)
      {
         SchemaAttribute[] attrs = (SchemaAttribute[])
            schemaType.GetCustomAttributes(typeof(SchemaAttribute), true);
         if ( attrs.Length > 0 )
         {
            if ( String.IsNullOrEmpty(attrs[0].TargetNamespace) )
               return attrs[0].RootElement;
            return string.Format("{0}#{1}", attrs[0].TargetNamespace, attrs[0].RootElement);
         }
         return null;
      }

      #endregion // Private Methods


      #region IEnumerable<IBaseComponent> Members

      IEnumerator<IBaseComponent> IEnumerable<IBaseComponent>.GetEnumerator()
      {
         foreach ( PStage stage in _pipeline.Stages )
         {
            IEnumerator enumerator = stage.GetComponentEnumerator();
            while ( enumerator.MoveNext() )
            {
               yield return (IBaseComponent)enumerator.Current;
            }
         }
      }

      #endregion

      #region IEnumerable Members

      IEnumerator IEnumerable.GetEnumerator()
      {
         return ((IEnumerable<IBaseComponent>)this).GetEnumerator();
      }

      #endregion

   } // class BasePipelineWrapper

} // namespace Winterdom.BizTalk.PipelineTesting
