// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Database
{
    /// <summary>
    /// Controls reversible deletion of models without exposing database transactions.
    /// Implementations must resolve and update the stored model atomically.
    /// </summary>
    public interface IModelDeletionStore<in TModel>
    {
        /// <summary>
        /// Marks an existing model for deletion. Returns false for missing or already deleted models.
        /// Does not remove associated files.
        /// </summary>
        bool Delete(TModel model);

        /// <summary>
        /// Restores a deleted model. Missing and already active models are left unchanged.
        /// </summary>
        void Undelete(TModel model);
    }
}
