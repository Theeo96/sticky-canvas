using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace StickyCanvas {
 [DataContract] public sealed class WebNote { [DataMember] public Note Document;[DataMember] public string BaseHash; }
 [DataContract] public sealed class WebAsset { [DataMember] public string Id,Mime,Data;[DataMember] public int Width,Height; }
 [DataContract] public sealed class WebWorkspace { [DataMember] public int Version=1;[DataMember] public List<WebNote> Notes=new List<WebNote>();[DataMember] public List<WebAsset> Assets=new List<WebAsset>(); }
 [DataContract] public sealed class WebTextEdit { [DataMember] public string ElementId;[DataMember] public List<TextParagraph> Paragraphs; }
 [DataContract] public sealed class WebNoteEdit { [DataMember] public string NoteId,BaseHash;[DataMember] public long BaseRevision;[DataMember] public List<WebTextEdit> TextEdits=new List<WebTextEdit>(); }
 [DataContract] public sealed class WebChanges { [DataMember] public int Version=1;[DataMember] public List<WebNoteEdit> Notes=new List<WebNoteEdit>(); }
 public sealed class WebImportResult { public readonly List<Note> Applied=new List<Note>();public readonly List<string> Conflicts=new List<string>(); }
 public sealed partial class NoteWindow { public void AcceptWebNote(Note note){EndEditing();Record();Model=note;history.Record(note);Render();} }
 public sealed partial class Store {
  public static string DocumentHash(Note note){using(var bytes=new MemoryStream(Encoding.UTF8.GetBytes(Json.Write(note))))return Sha(bytes);}
  public void ExportWebWorkspace(string path){
   lock(gate){Rules.Require(ReadErrors.Count==0,"읽지 못한 메모를 먼저 확인하세요.");var workspace=new WebWorkspace();
    foreach(var note in Load().Where(n=>n.DeletedAt==null)){Rules.Validate(note);workspace.Notes.Add(new WebNote{Document=note,BaseHash=DocumentHash(note)});}
    long size=0;foreach(var id in workspace.Notes.SelectMany(n=>n.Document.Elements).Where(e=>e.Type=="image").Select(e=>e.AssetId).Distinct()){
     var asset=GetAsset(id);Rules.Require(asset!=null,"첨부 누락");size+=asset.Bytes;Rules.Require(size<=64L*1024*1024,"웹 작업 파일의 첨부 합계는 64MiB까지 지원합니다.");var bytes=File.ReadAllBytes(AssetPath(asset));using(var input=new MemoryStream(bytes))Rules.Require(Sha(input)==id,"첨부 해시 불일치");
     workspace.Assets.Add(new WebAsset{Id=id,Mime=asset.FileName.EndsWith(".png")?"image/png":asset.FileName.EndsWith(".webp")?"image/webp":"image/jpeg",Data=Convert.ToBase64String(bytes),Width=asset.PixelWidth,Height=asset.PixelHeight});
    }
    string json=Json.Write(workspace);Rules.Require(Encoding.UTF8.GetByteCount(json)<=100L*1024*1024,"웹 작업 파일 크기 제한 초과");string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,json,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
   }
  }
  public WebImportResult ApplyWebChanges(WebChanges changes){
   Rules.Require(changes!=null&&changes.Version==1&&changes.Notes!=null&&changes.Notes.Count<=5000,"웹 변경 형식 오류");var result=new WebImportResult();
   lock(gate){db.Transaction(delegate{var ids=new HashSet<string>();foreach(var patch in changes.Notes){Rules.Require(patch!=null&&Rules.Id(patch.NoteId)&&Rules.Hash(patch.BaseHash)&&patch.TextEdits!=null&&patch.TextEdits.Count<=1000&&ids.Add(patch.NoteId),"중복 또는 잘못된 메모 변경");var rows=db.Query("SELECT json FROM notes WHERE id=?",1,patch.NoteId);
     if(rows.Count==0){result.Conflicts.Add(patch.NoteId);continue;}var note=Json.Read<Note>(rows[0][0]);Rules.Validate(note);Rules.Require(note.Id==patch.NoteId,"메모 ID 불일치");
     if(note.DeletedAt!=null||note.Revision!=patch.BaseRevision||DocumentHash(note)!=patch.BaseHash){result.Conflicts.Add(patch.NoteId);continue;}
     var seen=new HashSet<string>();foreach(var edit in patch.TextEdits){Rules.Require(edit!=null&&edit.Paragraphs!=null&&seen.Add(edit.ElementId),"중복 또는 잘못된 텍스트 변경");var e=note.Elements.FirstOrDefault(v=>v.Id==edit.ElementId);Rules.Require(e!=null&&e.Type=="text"&&!e.Locked,"편집할 수 없는 글상자");e.Paragraphs=edit.Paragraphs;}
     if(patch.TextEdits.Count==0)continue;
     note.Revision++;note.UpdatedAt=DateTime.UtcNow.ToString("o");Rules.Validate(note);result.Applied.Add(note);
    }
    if(result.Conflicts.Count>0){result.Applied.Clear();return;}
    foreach(var note in result.Applied)db.Execute("UPDATE notes SET json=? WHERE id=?",Json.Write(note),note.Id);});return result;
   }
  }
 }
}
