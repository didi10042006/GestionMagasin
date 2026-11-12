#define AppName "Gestion-Magasin"
#define AppVersion "1.0.0"
#define AppExeName "GestionMagasin.exe"

; Les bases et sauvegardes restent dans %LOCALAPPDATA%\GestionMagasin, hors du dossier programme.
; Aucune directive UninstallDelete/UninstallRun ne cible ces données.
; Une mise à jour remplace uniquement les fichiers de programme et préserve les bases commerciales.

[Setup]
AppId={{A4DF30D1-9C3D-49BC-93CF-39DDDCAD71D2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gestion-Magasin contributors
AppCopyright=Copyright (C) 2026 Gestion-Magasin contributors
DefaultDirName={localappdata}\Programs\Gestion-Magasin
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=GestionMagasin\Resources\gestion-magasin.ico
WizardSmallImageFile=Installer\wizard-small.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UsePreviousAppDir=yes
OutputDir=artifacts\installer
OutputBaseFilename=Gestion-Magasin-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; GroupDescription: "Raccourcis supplémentaires :"; Flags: unchecked

[Files]
Source: "artifacts\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "CONDITIONS-UTILISATION.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "CONDITIONS-UTILISATION.txt"; Flags: dontcopy

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Code]
var
  TermsPage: TWizardPage;
  TermsMemo: TNewMemo;
  AcceptTerms: TNewCheckBox;

procedure AcceptTermsClick(Sender: TObject);
begin
  if WizardForm.CurPageID = TermsPage.ID then
    WizardForm.NextButton.Enabled := AcceptTerms.Checked;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if WizardSilent then
  begin
    MsgBox('L''installation nécessite la lecture et l''acceptation des conditions d''utilisation. Le mode silencieux est désactivé.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
var
  TermsText: String;
  TermsLines: TArrayOfString;
  I: Integer;
begin
  ExtractTemporaryFile('CONDITIONS-UTILISATION.txt');
  if not LoadStringsFromFile(ExpandConstant('{tmp}\CONDITIONS-UTILISATION.txt'), TermsLines) then
    RaiseException('Impossible de charger les conditions d''utilisation. Installation interrompue.');
  for I := 0 to GetArrayLength(TermsLines) - 1 do
    TermsText := TermsText + TermsLines[I] + #13#10;

  TermsPage := CreateCustomPage(wpWelcome, 'Conditions d''utilisation', 'Veuillez lire les conditions avant de poursuivre l''installation.');

  TermsMemo := TNewMemo.Create(TermsPage);
  TermsMemo.Parent := TermsPage.Surface;
  TermsMemo.Left := 0;
  TermsMemo.Top := 0;
  TermsMemo.Width := TermsPage.SurfaceWidth;
  TermsMemo.Height := TermsPage.SurfaceHeight - ScaleY(42);
  TermsMemo.Anchors := [akLeft, akTop, akRight, akBottom];
  TermsMemo.ReadOnly := True;
  TermsMemo.ScrollBars := ssVertical;
  TermsMemo.WordWrap := True;
  TermsMemo.Text := TermsText;

  AcceptTerms := TNewCheckBox.Create(TermsPage);
  AcceptTerms.Parent := TermsPage.Surface;
  AcceptTerms.Left := 0;
  AcceptTerms.Top := TermsPage.SurfaceHeight - ScaleY(32);
  AcceptTerms.Width := TermsPage.SurfaceWidth;
  AcceptTerms.Height := ScaleY(24);
  AcceptTerms.Anchors := [akLeft, akRight, akBottom];
  AcceptTerms.Caption := 'J''accepte les conditions d''utilisation';
  AcceptTerms.Checked := False;
  AcceptTerms.OnClick := @AcceptTermsClick;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (TermsPage <> nil) and (CurPageID = TermsPage.ID) then
    WizardForm.NextButton.Enabled := AcceptTerms.Checked;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (TermsPage <> nil) and (CurPageID = TermsPage.ID) and not AcceptTerms.Checked then
  begin
    MsgBox('Vous devez cocher « J''accepte les conditions d''utilisation » pour continuer.', mbError, MB_OK);
    Result := False;
  end;
end;

