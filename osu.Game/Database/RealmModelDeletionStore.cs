// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Realms;

namespace osu.Game.Database
{
    internal sealed class RealmModelDeletionStore<TModel> : IModelDeletionStore<TModel>
        where TModel : RealmObject, IHasGuidPrimaryKey, ISoftDelete
    {
        private readonly RealmAccess realmAccess;

        public RealmModelDeletionStore(RealmAccess realmAccess)
        {
            this.realmAccess = realmAccess;
        }

        public bool Delete(TModel model) => setDeletePending(model, true);

        public void Undelete(TModel model) => setDeletePending(model, false);

        private bool setDeletePending(TModel model, bool value)
        {
            // Resolve inside the write transaction so concurrent imports cannot invalidate the lookup.
            return realmAccess.Write(realm =>
            {
                var stored = model.IsManaged ? model : realm.Find<TModel>(model.ID);
                if (stored == null || stored.DeletePending == value)
                    return false;

                stored.DeletePending = value;
                return true;
            });
        }
    }
}
