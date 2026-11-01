export function create() {
    const workers=new Map();let active;
    function getWorker(engine) {
        if(workers.has(engine))return workers.get(engine);
        const worker=new Worker(new URL('worker.js',import.meta.url));workers.set(engine,worker);
        worker.onmessage=({data})=>{
            if(workers.get(engine)!==worker || active?.worker!==worker || !active.pending)return;
            const pending=active.pending;active.pending=undefined;
            data.error?pending.reject(new Error(`${data.error.message}\n${data.error.stack||''}`)):pending.resolve(data.result);
        };
        worker.onerror=error=>{
            if(workers.get(engine)!==worker)return;
            const owner=active?.worker===worker?active:null,pending=owner?.pending;
            if(owner){owner.pending=null;owner.finish();}
            worker.terminate();workers.delete(engine);
            pending?.reject(new Error(error.message||'Pronunciation worker failed'));
        };
        return worker;
    }
    return {
        start(engine) {
            active?.cancel();
            const owner={worker:getWorker(engine),pending:null,
                cancel() {if(active!==owner)return;const pending=owner.pending;owner.pending=null;if(pending){owner.worker.terminate();workers.delete(engine);pending.reject(new Error('Pronunciation canceled'));}active=null;},
                finish(){if(active===owner){if(owner.pending)owner.cancel();else active=null;}},
                convert(text){if(active!==owner)throw Error('Superseded pronunciation command');if(owner.pending)throw Error('Pronunciation calls must be serialized');return new Promise((resolve,reject)=>{owner.pending={resolve,reject};owner.worker.postMessage({engine,text});});}
            };
            active=owner;return owner;
        },
        dispose(){active?.cancel();for(const worker of workers.values())worker.terminate();workers.clear();}
    };
}

