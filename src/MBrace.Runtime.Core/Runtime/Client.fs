﻿namespace MBrace.Runtime

open MBrace.Core
open MBrace.Core.Internals
open MBrace.Client

/// MBrace Sample runtime client instance.
[<AbstractClass>]
type MBraceClient () as self =

    let imem = lazy(LocalRuntime.Create(resources = self.Resources.ResourceRegistry))
    let processManager = lazy(new CloudProcessManager(self.Resources))

    abstract Resources : IRuntimeResourceManager

    /// Creates a fresh cloud cancellation token source for this runtime
    member c.CreateCancellationTokenSource (?parents : seq<ICloudCancellationToken>) : ICloudCancellationTokenSource =
        async {
            let parents = parents |> Option.map Seq.toArray
            let! dcts = DistributedCancellationToken.Create(c.Resources.CancellationEntryFactory, ?parents = parents, elevate = true)
            return dcts :> ICloudCancellationTokenSource
        } |> Async.RunSync

    /// <summary>
    ///     Asynchronously execute a workflow on the distributed runtime as task.
    /// </summary>
    /// <param name="workflow">Workflow to be executed.</param>
    /// <param name="cancellationToken">Cancellation token for computation.</param>
    /// <param name="faultPolicy">Fault policy. Defaults to single retry.</param>
    /// <param name="target">Target worker to initialize computation.</param>
    /// <param name="taskName">User-specified process name.</param>
    member c.CreateProcessAsync(workflow : Cloud<'T>, ?cancellationToken : ICloudCancellationToken, 
                                ?faultPolicy : FaultPolicy, ?target : IWorkerRef, ?taskName : string) : Async<CloudProcess<'T>> = async {

        let faultPolicy = match faultPolicy with Some fp -> fp | None -> FaultPolicy.Retry(maxRetries = 1)
        let dependencies = c.Resources.AssemblyManager.ComputeDependencies((workflow, faultPolicy))
        let assemblyIds = dependencies |> Array.map (fun d -> d.Id)
        do! c.Resources.AssemblyManager.UploadAssemblies(dependencies)
        let! tcs = Combinators.runStartAsCloudTask c.Resources assemblyIds taskName faultPolicy cancellationToken target workflow
        return processManager.Value.GetProcess tcs
    }

    /// <summary>
    ///     Execute a workflow on the distributed runtime as task.
    /// </summary>
    /// <param name="workflow">Workflow to be executed.</param>
    /// <param name="cancellationToken">Cancellation token for computation.</param>
    /// <param name="faultPolicy">Fault policy. Defaults to single retry.</param>
    /// <param name="target">Target worker to initialize computation.</param>
    /// <param name="taskName">User-specified process name.</param>
    member __.CreateProcess(workflow : Cloud<'T>, ?cancellationToken : ICloudCancellationToken, ?faultPolicy : FaultPolicy, ?target : IWorkerRef, ?taskName : string) : CloudProcess<'T> =
        __.CreateProcessAsync(workflow, ?cancellationToken = cancellationToken, ?faultPolicy = faultPolicy, ?target = target, ?taskName = taskName) |> Async.RunSync


    /// <summary>
    ///     Asynchronously executes a workflow on the distributed runtime.
    /// </summary>
    /// <param name="workflow">Workflow to be executed.</param>
    /// <param name="cancellationToken">Cancellation token for computation.</param>
    /// <param name="faultPolicy">Fault policy. Defaults to single retry.</param>
    /// <param name="target">Target worker to initialize computation.</param>
    /// <param name="taskName">User-specified process name.</param>
    member __.RunAsync(workflow : Cloud<'T>, ?cancellationToken : ICloudCancellationToken, ?faultPolicy : FaultPolicy, ?target : IWorkerRef, ?taskName : string) = async {
        let! task = __.CreateProcessAsync(workflow, ?cancellationToken = cancellationToken, ?faultPolicy = faultPolicy, ?target = target, ?taskName = taskName)
        return task.Result
    }

    /// <summary>
    ///     Execute a workflow on the distributed runtime synchronously
    /// </summary>
    /// <param name="workflow">Workflow to be executed.</param>
    /// <param name="cancellationToken">Cancellation token for computation.</param>
    /// <param name="faultPolicy">Fault policy. Defaults to single retry.</param>
    /// <param name="target">Target worker to initialize computation.</param>
    /// <param name="taskName">User-specified process name.</param>
    member __.Run(workflow : Cloud<'T>, ?cancellationToken : ICloudCancellationToken, ?faultPolicy : FaultPolicy, ?target : IWorkerRef, ?taskName : string) =
        __.RunAsync(workflow, ?cancellationToken = cancellationToken, ?faultPolicy = faultPolicy, ?target = target, ?taskName = taskName) |> Async.RunSync

    /// Gets all processes of provided cluster
    member __.GetAllProcesses () = processManager.Value.GetAllProcesses() |> Async.RunSync

    /// <summary>
    ///     Gets process object by process id.
    /// </summary>
    /// <param name="id">Task id.</param>
    member __.GetProcessById(id:string) = processManager.Value.GetProcessById(id) |> Async.RunSync

    /// <summary>
    ///     Clear cluster data for provided process.
    /// </summary>
    /// <param name="process">Process to be cleared.</param>
    member __.ClearProcess(p:CloudProcess) = processManager.Value.ClearProcess(p) |> Async.RunSync

    /// <summary>
    ///     Clear all process data from cluster.
    /// </summary>
    member __.ClearAllProcesses() = processManager.Value.ClearAllProcesses() |> Async.RunSync

    /// <summary>
    ///     Run workflow as local, in-memory computation
    /// </summary>
    /// <param name="workflow">Workflow to execute</param>
    member __.RunLocallyAsync(workflow : Cloud<'T>) : Async<'T> = imem.Value.RunAsync workflow

    /// Returns the store client for provided runtime.
    member __.StoreClient = imem.Value.StoreClient

    /// Gets all available workers for current runtime.
    member __.Workers = __.Resources.GetAvailableWorkers() |> Async.RunSynchronously

    /// <summary>
    ///     Run workflow as local, in-memory computation
    /// </summary>
    /// <param name="workflow">Workflow to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member __.RunLocally(workflow, ?cancellationToken) : 'T = imem.Value.Run(workflow, ?cancellationToken = cancellationToken)