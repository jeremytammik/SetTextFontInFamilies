#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#endregion

namespace SetTextFontInFamilies
{
  [Transaction( TransactionMode.Manual )]
  public class Command : IExternalCommand
  {
    const string _font_name = "Arial Black";
    const string _parameter_name = "Text Font";
    const BuiltInParameter _parameter_bip = BuiltInParameter.TEXT_FONT; // TEXT_STYLE_FONT

    class JtFamilyLoadOptions : IFamilyLoadOptions
    {
      public bool OnFamilyFound(
        bool familyInUse,
        out bool overwriteParameterValues )
      {
        overwriteParameterValues = true;
        return true;
      }

      public bool OnSharedFamilyFound(
        Family sharedFamily,
        bool familyInUse,
        out FamilySource source,
        out bool overwriteParameterValues )
      {
        source = FamilySource.Family;
        overwriteParameterValues = true;
        return true;
      }
    }

    /// <summary>
    /// Logging helper class to keep track of the result
    /// of updating the font of all text note types in a
    /// family. The family may be skipped or not. If not,
    /// keep track of all its text note types and a flag 
    /// for each indicating whether it was updated.
    /// </summary>
    class SetTextFontInFamilyResult
    {
      class TextNoteTypeResult
      {
        public string Name { get; set; }
        public bool Updated { get; set; }
      }

      /// <summary>
      /// The Family element name in the project database.
      /// </summary>
      public string FamilyName { get; set; }

      /// <summary>
      /// The family document used to reload the family.
      /// </summary>
      public Document FamilyDocument { get; set; }

      /// <summary>
      /// Was this family skipped, e.g. this family is not editable.
      /// </summary>
      public bool Skipped { get; set; }

      /// <summary>
      /// List of text note type names and updated flags.
      /// </summary>
      List<TextNoteTypeResult> TextNoteTypeResults;

      public SetTextFontInFamilyResult( Family f )
      {
        FamilyName = f.Name;
        TextNoteTypeResults = null;
      }

      public void AddTextNoteType(
        TextNoteType tnt,
        bool updated )
      {
        if( null == TextNoteTypeResults )
        {
          TextNoteTypeResults
            = new List<TextNoteTypeResult>();
        }
        TextNoteTypeResult r = new TextNoteTypeResult();
        r.Name = tnt.Name;
        r.Updated = updated;
        TextNoteTypeResults.Add( r );
      }

      int NumberOfUpdatedTextNoteTypes
      {
        get
        {
          return null == TextNoteTypeResults
            ? 0
            : TextNoteTypeResults
              .Count<TextNoteTypeResult>(
                r => r.Updated );
        }
      }

      public bool NeedsReload
      {
        get
        {
          return 0 < NumberOfUpdatedTextNoteTypes;
        }
      }

      public override string ToString()
      {
        // FamilyDocument.Title

        string s = FamilyName + ": ";

        if( Skipped )
        {
          s += "skipped";
        }
        else
        {
          int nTotal = TextNoteTypeResults.Count;
          int nUpdated = NumberOfUpdatedTextNoteTypes;

          s += string.Format(
            "{0} text note types processed, "
            + "{1} updated", nTotal, nUpdated );
        }
        return s;
      }
    }

    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Application app = uiapp.Application;
      Document doc = uidoc.Document;

      FilteredElementCollector families
        = new FilteredElementCollector( doc )
          .OfClass( typeof( Family ) );

      List<SetTextFontInFamilyResult> results
        = new List<SetTextFontInFamilyResult>();

      Document famdoc;
      SetTextFontInFamilyResult r1;
      bool updatedTextNoteStyle;

      foreach( Family f in families )
      {
        r1 = new SetTextFontInFamilyResult( f );

        bool updatedFamily = false;

        try
        {
          r1.FamilyDocument
            = famdoc
            = doc.EditFamily( f );

        }
        catch( Autodesk.Revit.Exceptions.ArgumentException ex )
        {
          r1.Skipped = true;
          results.Add( r1 );
          Debug.Print( "Family '{0}': {1}", f.Name, ex.Message );
          continue;
        }

        FilteredElementCollector textNoteTypes
          = new FilteredElementCollector( famdoc )
            .OfClass( typeof( TextNoteType ) );

        foreach( TextNoteType tnt in textNoteTypes )
        {
          updatedTextNoteStyle = false;

          // It is normally better to use the built-in
          // parameter enumeration value rather than
          // the parameter definition display name.
          // The latter is language dependent, possibly
          // returns multiple hits, and uses a less 
          // efficient string comparison.

          //Parameter p2 = tnt.get_Parameter( 
          //  _parameter_bip );

          IList<Parameter> ps = tnt.GetParameters(
            _parameter_name );

          Debug.Assert( 1 == ps.Count,
            "expected only one 'Text Font' parameter" );

          foreach( Parameter p in ps )
          {
            if( _font_name != p.AsString() )
            {
              using( Transaction tx
                = new Transaction( doc ) )
              {
                tx.Start( "Update Text Font" );
                p.Set( _font_name );
                tx.Commit();

                updatedFamily
                  = updatedTextNoteStyle
                  = true;
              }
            }
          }
          r1.AddTextNoteType( tnt, updatedTextNoteStyle );
        }
        results.Add( r1 );

        // This causes the iteration over the filtered 
        // element collector to throw an 
        // InvalidOperationException: The iterator cannot 
        // proceed due to changes made to the Element table 
        // in Revit's database (typically, This can be the 
        // result of an Element deletion).

        //if( updatedFamily )
        //{
        //  f2 = famdoc.LoadFamily(
        //    doc, new JtFamilyLoadOptions() );
        //}
      }

      // Reload modified families after terminating 
      // the filtered element collector iteration.

      IFamilyLoadOptions opt
        = new JtFamilyLoadOptions();

      Family f2;

      foreach( SetTextFontInFamilyResult r in results )
      {
        if( r.NeedsReload )
        {
          f2 = r.FamilyDocument.LoadFamily( doc, opt );
        }
      }

      TaskDialog d = new TaskDialog(
        "Set Text Note Font" );

      d.MainInstruction = string.Format(
        "{0} families processed.", results.Count );

      List<string> family_results
        = results.ConvertAll<string>(
          r => r.ToString() );

      family_results.Sort();

      d.MainContent = string.Join( "\r\n",
        family_results );

      d.Show();

      return Result.Succeeded;
    }
  }
}
