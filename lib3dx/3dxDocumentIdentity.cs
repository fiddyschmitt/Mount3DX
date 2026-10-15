namespace lib3dx
{
    //A document as 3DX describes it when asked about one of its ids: the id 3DX gave back, and the
    //type, name and revision, which identify a document as uniquely as its id does
    public sealed record _3dxDocumentIdentity(string Id, string? Name, string? Revision, string? TypeNls);
}
